#!/usr/bin/env python3
"""WPF binding checker (heuristic, complements tools/check-xaml.py).

A wrong binding path does not fail the build and does not throw at runtime - it silently produces an
empty control. That is exactly the class of defect that a WPF project without a Windows machine cannot
see, so it is checked here instead:

  * the root of every `{Binding Path}` is resolved against the view model of the file
    (`...Views/DashboardView.xaml` -> `DashboardViewModel`),
  * bindings inside a `<DataTemplate DataType="...">` are resolved against that type,
  * a template without a DataType is skipped and counted, never guessed.

The tool needs the member map of src/, which it takes from tools/check-contracts.py.

Usage:
    python3 tools/check-bindings.py
"""

from __future__ import annotations

import importlib.util
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
XAML = ROOT / "src"


def load_contracts():
    spec = importlib.util.spec_from_file_location("check_contracts", ROOT / "tools/check-contracts.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules["check_contracts"] = module
    spec.loader.exec_module(module)
    return module


TAG = re.compile(r'''<(?P<closing>/)?(?P<name>[A-Za-z_][\w:.]*)(?P<attributes>(?:"[^"]*"|[^>])*?)(?P<self>/)?>''', re.S)
DATA_TYPE = re.compile(r'DataType\s*=\s*"(?:\{x:Type\s+)?(?:[\w]+:)?(?P<name>[A-Za-z_][\w]*)\}?"')
ITEMS_SOURCE = re.compile(r'ItemsSource\s*=\s*"\{Binding\s+(?:Path=)?(?P<path>[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)')
BINDING = re.compile(r"\{Binding\s+(?:Path=)?(?P<path>[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)?\s*(?:,|\})")
ITEMS_CONTROL_KINDS = {"ListView", "ListBox", "ItemsControl", "DataGrid", "DataGridTemplateColumn"}


def strip_comments(text: str) -> str:
    """Removes XML comments, which may contain < or > and would confuse the tag scanner."""
    out = text
    while "<!--" in out:
        start = out.index("<!--")
        end = out.find("-->", start)
        if end < 0:
            return out[:start]
        out = out[:start] + " " * (end + 3 - start) + out[end + 3:]
    return out


def scopes_of(text: str, types, view_model: str | None) -> list[tuple[int, int, str | None, int]]:
    """(start, end, item type name or None, end of the opening tag) for every element with a scope.

    A scope comes from `<DataTemplate DataType="X">` or from the element type of the collection that
    an items control is bound to. Children without their own scope inherit the enclosing one, which
    is what makes `DisplayMemberBinding` inside a GridView work.
    """
    scopes: list[tuple[int, int, str | None, int]] = []
    stack: list[tuple[str, int, str | None, bool, int]] = []

    for match in TAG.finditer(text):
        name = match.group("name").split(":")[-1]
        attributes = match.group("attributes")
        closing = match.group("closing") is not None
        self_closing = match.group("self") is not None

        if closing:
            while stack:
                opened_name, start, scope_type, introduced, tag_end = stack.pop()
                if introduced:
                    # The attributes of the element itself belong to the *enclosing* scope: an
                    # ItemsSource binding names the collection on the view model, not on the item.
                    scopes.append((tag_end, match.end(), scope_type, tag_end))
                if opened_name == name:
                    break
            continue

        introduced = False
        scope_type: str | None = None
        if name == "DataTemplate":
            type_match = DATA_TYPE.search(attributes)
            if type_match:
                scope_type = type_match.group("name")
                introduced = True
        elif name in ITEMS_CONTROL_KINDS:
            source = ITEMS_SOURCE.search(attributes)
            if source and view_model and source.group("path").split(".")[0] in (types.get(view_model).members if types.get(view_model) else set()):
                root = source.group("path").split(".")[0]
                property_type = types[view_model].member_types.get(root)
                if property_type:
                    scope_type = unwrap(property_type)
                    introduced = True

        stack.append((name, match.start(), scope_type, introduced, match.end()))

        if self_closing:
            opened_name, start, opened_scope, was_introduced, tag_end = stack.pop()
            if was_introduced:
                scopes.append((tag_end, match.end(), opened_scope, tag_end))

    # Elements that never close (self contained files) still count as a scope up to the end.
    for opened_name, start, scope_type, introduced, tag_end in stack:
        if introduced:
            scopes.append((tag_end, len(text), scope_type, tag_end))
    return scopes


def unwrap(type_name: str) -> str:
    """Element type of a collection: `BulkObservableCollection<Problem>` becomes `Problem`."""
    inner = re.search(r"<\s*(?P<inner>[A-Za-z0-9_]+)\s*>", type_name)
    if inner and "ObservableCollection" in type_name or (inner and "IReadOnlyList" in type_name):
        return inner.group("inner")
    if inner:
        return inner.group("inner")
    return type_name.strip().rstrip("?")


def members_of(types, name: str) -> set[str] | None:
    info = types.get(name)
    if info is None:
        return None
    collected = set(info.members)
    pending = [base.split(".")[-1] for base in info.bases]
    seen = {name}
    while pending:
        current = pending.pop()
        if current in seen:
            continue
        seen.add(current)
        base_info = types.get(current)
        if base_info is None:
            continue
        collected |= base_info.members
        pending += [base.split(".")[-1] for base in base_info.bases]
    return collected


def main() -> int:
    contracts = load_contracts()
    files = sorted((ROOT / "src").rglob("*.cs"))
    if not files:
        print("no source files found", file=sys.stderr)
        return 2
    types = contracts.collect_types(files)

    view_models = [name for name in types if name.endswith("ViewModel")]
    findings: list[str] = []
    checked = 0
    skipped = 0

    for path in sorted(XAML.rglob("*.xaml")):
        text = path.read_text(encoding="utf-8")

        class_match = re.search(r'x:Class="[\w\.]*\.(?P<name>[A-Za-z_]\w*)"', text)
        view_model = None
        if class_match:
            stem = class_match.group("name")
            stem = stem[:-4] if stem.endswith("View") else stem
            candidate = stem + "ViewModel"
            if candidate in view_models:
                view_model = candidate
            elif stem == "MainWindow" and "MainViewModel" in view_models:
                view_model = "MainViewModel"

        scopes = scopes_of(text, types, view_model)
        for match in BINDING.finditer(text):
            binding_path = match.group("path")
            if not binding_path:
                continue

            root = binding_path.split(".")[0]
            start = match.start()
            enclosing = [scope for scope in scopes if scope[0] <= start <= scope[1] and start > scope[3]]
            if enclosing:
                # the innermost element that introduced a data scope wins
                scope_type = sorted(enclosing, key=lambda scope: scope[1] - scope[0])[0][2]
                if scope_type is None:
                    skipped += 1
                    continue
            else:
                scope_type = view_model
                if scope_type is None:
                    skipped += 1
                    continue

            available = members_of(types, scope_type)
            if available is None:
                skipped += 1
                continue

            checked += 1
            if root not in available:
                findings.append(
                    f"{path.relative_to(ROOT)}: '{root}' (binding '{binding_path}') does not exist on {scope_type}"
                )

    print(f"inspected {len(list(XAML.rglob('*.xaml')))} XAML file(s): {checked} binding(s) resolved, {skipped} skipped (no DataType/view model information)")
    if findings:
        print(f"\n{len(findings)} finding(s):")
        for item in sorted(set(findings)):
            print("  -", item)
        return 1

    print("no binding problems detected (heuristic check only - the WPF binding engine is the authority)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
