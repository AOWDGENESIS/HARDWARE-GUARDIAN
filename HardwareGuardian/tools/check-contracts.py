#!/usr/bin/env python3
"""Heuristic contract checker for Hardware Guardian.

This is NOT a compiler and NOT a test. It exists because the sandbox has no .NET SDK, and it
catches exactly the class of defect that was found by hand before: module code that was written
against an assumed contract (properties that do not exist on a Core type, interfaces that are not
implemented in full, enum members that are not defined).

What it does:
  1. builds a member map of every type declared under src/ (properties, methods, fields, enum members)
  2. checks every object initialiser  new T { Prop = ... }  against the declared members of T
  3. checks every enum member access      Enum.Member
  4. checks every static member access    StaticClass.Member
  5. checks every `local.Member` whose local type is resolvable inside this repository
  6. checks that each `class X : IFoo` implements every member of the interface IFoo
     (src/ and, when present, tests/)

Exit code 1 when findings exist. Run:  python3 tools/check-contracts.py
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
TESTS = ROOT / "tests"

DECL = re.compile(
    r"^\s*(?:public|internal|private|protected)?\s*"
    r"(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|ref\s+|file\s+|unsafe\s+)*"
    r"(?P<kind>class|record|struct|interface|enum)\s+"
    # `record struct X` / `record class X` declare X, not a type named "struct": without this the
    # member map contained a bogus type "struct" and the real record was missing entirely.
    r"(?:(?:class|struct)\s+)?"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)"
    r"(?P<rest>[^{;]*)",
    re.M,
)

INTERFACE_MEMBER = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?P<type>[A-Za-z_][\w\.\<\>\[\]\,\?]*)\s+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*"
    r"(?:<[^;{}()]*>)?\s*"
    r"(?:\{|=>|\(|;|\})",
    re.M,
)

MEMBER = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?:public|internal|protected|private)\s+"
    r"(?:(?:static|virtual|abstract|override|readonly|sealed|partial|async|required|new|extern|unsafe|const|event)\s+)*"
    r"(?P<type>[A-Za-z_][\w\.]*(?:<[^;=(){}]*>)?(?:\[\])*(?:\?)?)\s+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*"
    r"(?:<[^;=(){}]*>)?\s*"
    r"(?P<tail>\{|=>|=|;|\()",
    re.M,
)

METHOD = re.compile(
    r"^\s*(?:public|internal|protected|private)?\s*(?:static\s+|virtual\s+|abstract\s+|override\s+|"
    r"sealed\s+|partial\s+|async\s+|new\s+)*"
    r"(?:[\w\.\<\>\[\]\?\,]+\s+)?(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
    re.M,
)




METHOD_FALLBACK = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|protected|private)\s+"
    r"[^;{}=]*?\b(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^;=(){}]*>)?\s*\(",
    re.M,
)

def top_level_assignments(body: str) -> list[str]:
    """Property names assigned directly in an object initialiser (ignores nested initialisers)."""
    names: list[str] = []
    depth = 0
    i = 0
    length = len(body)
    while i < length:
        char = body[i]
        if char in "([{":
            depth += 1
            i += 1
            continue
        if char in ")]}":
            depth -= 1
            i += 1
            continue
        if depth == 0:
            match = re.match(r"([A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)", body[i:])
            if match:
                names.append(match.group(1))
                i += match.end()
                continue
            # skip a leading comma or whitespace
            match = re.match(r"[\s,]+", body[i:])
            if match:
                i += match.end()
                continue
            # unknown token: advance one character so that braces keep being tracked
            i += 1
            continue
        i += 1
    return [name for name in names if name != "_"]


@dataclass
class TypeInfo:
    name: str
    kind: str
    namespace: str
    file: str
    members: set[str] = field(default_factory=set)
    member_types: dict[str, str] = field(default_factory=dict)
    bases: list[str] = field(default_factory=list)
    is_static: bool = False


def strip_comments_and_strings(text: str) -> str:
    """Single pass replacement of comments and literals by neutral placeholders.

    A naive "remove // comments" pass destroys every string that contains a URL such as
    "https://...", which silently unbalanced the brace counting and produced false findings.
    """
    out: list[str] = []
    i = 0
    length = len(text)

    while i < length:
        char = text[i]

        # block comment
        if char == "/" and i + 1 < length and text[i + 1] == "*":
            end_index = text.find("*/", i + 2)
            end_index = length if end_index < 0 else end_index + 2
            out.append(" " * (end_index - i))
            i = end_index
            continue

        # line comment
        if char == "/" and i + 1 < length and text[i + 1] == "/":
            end_index = text.find("\n", i)
            end_index = length if end_index < 0 else end_index
            out.append(" " * (end_index - i))
            i = end_index
            continue

        # verbatim string
        if char == "@" and i + 1 < length and text[i + 1] == '"':
            j = i + 2
            while j < length:
                if text[j] == '"':
                    if j + 1 < length and text[j + 1] == '"':
                        j += 2
                        continue
                    j += 1
                    break
                j += 1
            out.append('""')
            i = j
            continue

        # interpolated or regular string
        if char == "$" and i + 1 < length and text[i + 1] == '"':
            j = i + 2
            while j < length:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    j += 1
                    break
                j += 1
            out.append('""')
            i = j
            continue

        if char == '"':
            j = i + 1
            while j < length:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    j += 1
                    break
                j += 1
            out.append('""')
            i = j
            continue

        # character literal
        if char == "'":
            j = i + 1
            while j < length:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == "'":
                    j += 1
                    break
                j += 1
            out.append("''")
            i = j
            continue

        out.append(char)
        i += 1

    return "".join(out)


def body_of(text: str, start: int) -> str:
    """Returns the brace-balanced body that starts at or after `start`."""
    i = text.find("{", start)
    if i < 0:
        return ""
    depth = 0
    for j in range(i, len(text)):
        if text[j] == "{":
            depth += 1
        elif text[j] == "}":
            depth -= 1
            if depth == 0:
                return text[i + 1 : j]
    return text[i + 1 :]


def collect_types(files: list[Path]) -> dict[str, TypeInfo]:
    types: dict[str, TypeInfo] = {}
    for path in files:
        raw = path.read_text(encoding="utf-8", errors="replace")
        text = strip_comments_and_strings(raw)
        namespace = "?"
        match = re.search(r"namespace\s+([\w\.]+)", text)
        if match:
            namespace = match.group(1)

        for decl in DECL.finditer(text):
            name = decl.group("name")
            kind = decl.group("kind")
            rest = decl.group("rest")
            info = TypeInfo(
                name=name,
                kind=kind,
                namespace=namespace,
                file=str(path.relative_to(ROOT)),
                is_static="static class" in decl.group(0) or "static record" in decl.group(0),
            )

            bases = re.findall(r":\s*([^{]+)", rest)
            for base in bases:
                for part in base.split(","):
                    candidate = re.sub(r"<.*", "", part).strip()
                    candidate = re.sub(r"\bwhere\b.*", "", candidate).strip()
                    if candidate and candidate not in {"class", "struct"}:
                        info.bases.append(candidate)

            if kind == "interface":
                # Interface members carry no access modifier, so MEMBER (which requires one) would
                # collect nothing - that made the implementation check silently ineffective.
                body = body_of(text, decl.end())
                body = re.sub(r"\bevent\s+", "", body)
                info.members.update(m.group("name") for m in INTERFACE_MEMBER.finditer(body))
                types[name] = info
                continue

            if kind == "enum":
                body = body_of(text, decl.end())
                info.members.update(re.findall(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*[^,]+)?,", body, re.M))
                info.members.update(re.findall(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*[^,\n]+)?$", body, re.M))
            else:
                body = body_of(text, decl.end())
                for member in MEMBER.finditer(body):
                    info.members.add(member.group("name"))
                    info.member_types[member.group("name")] = member.group("type")
                for method in METHOD.finditer(body):
                    info.members.add(method.group("name"))
                for method in METHOD_FALLBACK.finditer(body):
                    info.members.add(method.group("name"))
                # records: positional parameters are properties as well
                params = re.search(r"\(([^)]*)\)", rest)
                if params and kind == "record":
                    for part in params.group(1).split(","):
                        tokens = part.strip().split("=")[0].strip().split()
                        if len(tokens) >= 2 and re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", tokens[-1]):
                            info.members.add(tokens[-1])
                            info.member_types[tokens[-1]] = tokens[-2]

            types[name] = info
    return types


def check_object_initialisers(files: list[Path], types: dict[str, TypeInfo]) -> list[str]:
    findings = []
    pattern = re.compile(r"new\s+([A-Za-z_][\w\.]*(?:<[^;{]*?>)?)\s*(?:\([^)]*\))?\s*\{")
    for path in files:
        text = strip_comments_and_strings(path.read_text(encoding="utf-8", errors="replace"))
        for m in pattern.finditer(text):
            raw_type = m.group(1)
            name = re.sub(r"<.*", "", raw_type).split(".")[-1]
            info = types.get(name)
            if info is None or info.kind == "enum":
                continue
            body = body_of(text, m.end() - 1)
            assignments = top_level_assignments(body)
            for prop in assignments:
                if prop in info.members:
                    continue
                findings.append(
                    f"{path.relative_to(ROOT)}: '{prop}' is not a member of {name} (declared in {info.file})"
                )
    return findings


def check_member_access(files: list[Path], types: dict[str, TypeInfo]) -> list[str]:
    findings = []
    # Members that every type inherits from System.Object / System.Enum (or that the record and
    # struct synthesiser supplies) are not declared anywhere in the sources, so the member map
    # cannot know them. Without this exemption `SomeEnum.Value.ToString()` is reported as a typo -
    # that produced two false findings on the reporting module.
    inherited = {
        "ToString", "GetHashCode", "Equals", "GetType", "GetTypeCode", "CompareTo", "HasFlag",
        "ReferenceEquals", "Deconstruct", "PrintMembers",
    }
    access = re.compile(r"\b([A-Z][A-Za-z0-9_]*)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)")
    for path in files:
        text = strip_comments_and_strings(path.read_text(encoding="utf-8", errors="replace"))
        for m in access.finditer(text):
            owner, member = m.group(1), m.group(2)
            info = types.get(owner)
            if info is None or member in inherited:
                continue
            if info.kind == "enum" and member not in info.members:
                findings.append(f"{path.relative_to(ROOT)}: enum {owner} has no member '{member}' (declared in {info.file})")
            elif info.kind == "class" and info.is_static and member not in info.members:
                findings.append(f"{path.relative_to(ROOT)}: static class {owner} has no member '{member}' (declared in {info.file})")
    return findings


INHERITED_MEMBERS = {
    "ToString", "GetHashCode", "Equals", "GetType", "GetTypeCode", "CompareTo", "HasFlag",
    "ReferenceEquals", "Deconstruct", "PrintMembers", "Value", "Key", "Name", "Length", "Count",
    # System.Linq and the collection interfaces are library surface, not declared here:
    "First", "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault", "Where",
    "Select", "SelectMany", "Any", "All", "ToList", "ToArray", "ToHashSet", "ToDictionary",
    "OrderBy", "OrderByDescending", "ThenBy", "Sum", "Max", "Min", "Average", "Skip", "Take",
    "Distinct", "Contains", "Cast", "OfType", "GroupBy", "Aggregate", "ElementAt",
    "ElementAtOrDefault", "Append", "Prepend", "AsEnumerable", "Reverse", "Chunk", "Add",
    "AddRange", "Remove", "RemoveAt", "Clear", "Insert", "TryGetValue", "Sort", "ForEach",
}

TYPED_LOCAL = re.compile(
    r"\b(?P<type>[A-Z][A-Za-z0-9_]*(?:<[^;{}()]*>)?)\s+(?P<name>[a-z_][A-Za-z0-9_]*)\s*(?P<tail>=|;|\)|,)"
)
FOREACH = re.compile(
    r"foreach\s*\(\s*var\s+(?P<name>[a-z_][A-Za-z0-9_]*)\s+in\s+(?P<source>[A-Za-z0-9_.]+)\s*\)"
)
ELEMENT = re.compile(r"<\s*(?P<inner>[A-Za-z0-9_]+)\s*>")
EXTENSION = re.compile(
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^()]*>)?\s*\(\s*this\s+"
    r"(?P<type>[A-Z][A-Za-z0-9_]*(?:<[^()]*>)?)\s+(?:[A-Za-z_][A-Za-z0-9_]*)\s*[,)]"
)
VAR_DECL = re.compile(r"\bvar\s+(?P<name>[a-z_][A-Za-z0-9_]*)\s*=\s*(?P<rhs>[^;\n]*)")
WRAPPER = re.compile(r"^(?:Task|ValueTask|IReadOnlyList|IEnumerable|IList|List|ICollection|HashSet|IReadOnlyCollection)<\s*(?P<inner>[A-Za-z0-9_]+)\s*>$")
METHOD_START = re.compile(
    r"^[ \t]*(?:\[[^\]]*\]\s*)*"
    r"(?:public|internal|protected|private)\s+"
    r"(?:(?:static|virtual|abstract|override|sealed|partial|async|new|unsafe|extern|required)\s+)*"
    r"[^\n;{}]*?\b(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
    re.M,
)
KEYWORDS = {
    "if", "while", "for", "foreach", "switch", "catch", "using", "lock", "fixed", "else", "do",
    "return", "new", "get", "set", "init", "operator", "checked", "unchecked", "when", "await",
    "throw", "yield", "case", "default", "nameof", "sizeof", "typeof", "base", "this", "where",
}


def evaluate(expression: str, types: dict[str, TypeInfo], scope: dict[str, set[str]]) -> str | None:
    """Resolves the type of a member chain such as `_hash.ComputeFileHashAsync` or `snapshot.Storage`.

    Nothing is guessed: an unresolvable link ends the resolution and the receiver stays unchecked.
    """
    text = re.sub(r"^await\s+", "", expression.strip())
    text = text.split("(")[0]
    parts = [p for p in re.split(r"\??\.", text) if p]
    if not parts:
        return None
    candidates = scope.get(parts[0])
    if not candidates or len(candidates) != 1:
        return None
    resolved = next(iter(candidates))
    for part in parts[1:]:
        info = types.get(resolved)
        if info is None:
            return None
        member_type = info.member_types.get(part)
        if member_type is None:
            for base in info.bases:
                base_info = types.get(base.split(".")[-1])
                if base_info and part in base_info.member_types:
                    member_type = base_info.member_types[part]
                    break
        if member_type is None:
            return None
        resolved = unwrap(member_type)
        if resolved not in types:
            return None
    return resolved


def unwrap(type_name: str) -> str:
    """Removes wrappers and nullability: `Task<HashResult>?` becomes `HashResult`."""
    candidate = type_name.strip().rstrip("?").strip()
    for _ in range(4):
        match = WRAPPER.match(candidate)
        if match:
            candidate = match.group("inner")
            continue
        inner = ELEMENT.search(candidate)
        if inner:
            candidate = inner.group("inner")
            continue
        break
    return candidate


def parameters_of(text: str, open_paren: int) -> str:
    """Returns the parameter list that belongs to the parenthesis at open_paren."""
    depth = 0
    for i in range(open_paren, len(text)):
        if text[i] == "(":
            depth += 1
        elif text[i] == ")":
            depth -= 1
            if depth == 0:
                return text[open_paren + 1 : i]
    return ""


def direct_body(text: str, start: int) -> str:
    """Body of the brace that starts at `start` (start must point at the opening brace)."""
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start + 1 : i]
    return text[start + 1 :]


def method_regions(text: str) -> list[tuple[str, str, int, int, int]]:
    """(name, parameters, signature start, body start, body end) for every method that has a body.

    Candidates without a parameter list that is followed by a body are rejected, so an expression
    body such as `public string Name => Format(x);` is not mistaken for a method.
    """
    regions = []
    for m in METHOD_START.finditer(text):
        if m.group("name") in KEYWORDS:
            continue
        open_paren = text.find("(", m.end() - 1)
        if open_paren < 0:
            continue
        parameters = parameters_of(text, open_paren)
        after = open_paren + len(parameters) + 2
        rest = text[after : after + 80]
        stripped = rest.lstrip()
        if stripped.startswith("{"):
            start = after + (len(rest) - len(stripped))
            regions.append((m.group("name"), parameters, m.start(), start, start + len(direct_body(text, start)) + 2))
        elif stripped.startswith("=>"):
            end = text.find(";", after)
            if end >= 0:
                regions.append((m.group("name"), parameters, m.start(), after, end))
        elif stripped.startswith("where"):
            brace = text.find("{", after)
            if brace >= 0:
                regions.append((m.group("name"), parameters, m.start(), brace, brace + len(direct_body(text, brace)) + 2))
    return regions


def check_typed_member_access(files: list[Path], types: dict[str, TypeInfo]) -> list[str]:
    """Validates `local.Member` against the declared type of the local.

    This catches the defect class that was found by hand several times: a member was used on a Core
    type that does not declare it. The scope is built per method, because the same local name is
    reused with different types in different methods; a name that stays ambiguous is skipped instead
    of guessed. Only receivers whose type is declared in this repository are checked, and every
    extension method declared here is exempt.
    """
    findings: list[str] = []
    extensions: dict[str, set[str]] = {}
    for path in files:
        text = strip_comments_and_strings(path.read_text(encoding="utf-8", errors="replace"))
        for decl in DECL.finditer(text):
            if "static" not in decl.group(0):
                continue
            for m in EXTENSION.finditer(body_of(text, decl.end())):
                extensions.setdefault(re.sub(r"<.*", "", m.group("type")), set()).add(m.group("name"))

    def bind(scope: dict[str, set[str]], name: str, type_name: str) -> None:
        base = re.sub(r"<.*", "", type_name).strip().rstrip("?").strip()
        if base in types:
            scope.setdefault(name, set()).add(base)

    def collect(region: str, base: dict[str, set[str]] | None = None) -> dict[str, set[str]]:
        found: dict[str, set[str]] = {k: set(v) for k, v in (base or {}).items()}
        for m in TYPED_LOCAL.finditer(region):
            bind(found, m.group("name"), m.group("type"))
        for m in VAR_DECL.finditer(region):
            expression = m.group("rhs").strip()
            if expression.startswith("new "):
                bind(found, m.group("name"), expression[4:].split("(")[0])
            else:
                resolved = evaluate(expression, types, found)
                if resolved:
                    bind(found, m.group("name"), resolved)
                else:
                    found.pop(m.group("name"), None)
        for m in FOREACH.finditer(region):
            source = evaluate(m.group("source"), types, found)
            if source:
                bind(found, m.group("name"), source)
        return found

    def check_region(region: str, scope: dict[str, str], path: Path) -> None:
        access = re.compile(r"\b(?P<name>[a-z_][A-Za-z0-9_]*)\s*\.\s*(?P<member>[A-Za-z_][A-Za-z0-9_]*)")
        for m in access.finditer(region):
            owner = scope.get(m.group("name"))
            member = m.group("member")
            if owner is None:
                continue
            info = types[owner]
            if info.kind == "enum" or member in info.members:
                continue
            if member in extensions.get(owner, set()) or member in INHERITED_MEMBERS:
                continue
            declared: set[str] = set()
            visited: set[str] = set()
            pending = [info]
            while pending:
                current = pending.pop()
                if current.name in visited:
                    continue
                visited.add(current.name)
                declared |= current.members
                pending += [types[b.split(".")[-1]] for b in current.bases if b.split(".")[-1] in types]
            if member in declared:
                continue
            findings.append(
                f"{path.relative_to(ROOT)}: {owner} has no member '{member}' "
                f"(receiver '{m.group('name')}', declared in {info.file})"
            )

    for path in files:
        text = strip_comments_and_strings(path.read_text(encoding="utf-8", errors="replace"))
        regions = method_regions(text)
        # Nested types and local functions mean that one region can contain another. Only the
        # innermost regions are used, otherwise the parameter names of a nested method leak into the
        # scope of the outer one.
        regions = [
            region for region in regions
            if not any(other[3] <= region[3] and region[4] <= other[4] and other[3] != region[3] for other in regions)
        ]

        # Field declarations are collected from the text outside every method body; a declaration
        # inside a body is a local and must not become a scope for other methods.
        blanked = text
        for _, _, signature, start, end in regions:
            blanked = blanked[:signature] + " " * (end - signature) + blanked[end:]
        fields = collect(blanked)
        field_scope = {k: next(iter(v)) for k, v in fields.items() if len(v) == 1}

        for _, parameters, _, start, end in regions:
            body = text[start:end]
            # The trailing comma makes a single parameter ("SystemSnapshot snapshot") match as well:
            # the declaration pattern needs a delimiter after the name.
            local = collect(parameters + ",", {k: {v} for k, v in field_scope.items()})
            merged = collect(body, local)
            scope = {k: next(iter(v)) for k, v in merged.items() if len(v) == 1}
            check_region(body, scope, path)
    return findings


def check_interface_implementation(files: list[Path], types: dict[str, TypeInfo]) -> list[str]:
    findings = []
    for path in files:
        text = strip_comments_and_strings(path.read_text(encoding="utf-8", errors="replace"))
        for decl in DECL.finditer(text):
            if decl.group("kind") not in {"class", "record", "struct"}:
                continue
            name = decl.group("name")
            info = types.get(name)
            if info is None:
                continue
            for base in info.bases:
                target = types.get(base.split(".")[-1])
                if target is None or target.kind != "interface":
                    continue
                missing = sorted(member for member in target.members if member not in info.members)
                # property accessors and event handlers appear as add/remove; ignore those
                missing = [m for m in missing if m not in {"add", "remove", "get", "set"} and m != name]
                if missing:
                    findings.append(
                        f"{path.relative_to(ROOT)}: {name} does not appear to implement {base}: missing {', '.join(missing)}"
                    )
    return findings


def main() -> int:
    files = sorted(SRC.rglob("*.cs"))
    if TESTS.is_dir():
        # The test project is checked against the same member map: it is the place where a wrong
        # contract is cheapest to catch, and the test doubles have to implement their interfaces.
        files += sorted(TESTS.rglob("*.cs"))
    if not files:
        print("no source files found", file=sys.stderr)
        return 2

    types = collect_types(files)
    print(f"inspected {len(files)} files, {len(types)} declared types")

    # Optional diagnosis of the member map itself: --types Foo,Bar
    if len(sys.argv) > 2 and sys.argv[1] == "--types":
        for name in sys.argv[2].split(","):
            info = types.get(name.strip())
            if info is None:
                print(f"{name}: not declared")
                continue
            print(f"{name}: kind={info.kind} static={info.is_static} bases={info.bases}")
            print(f"   members({len(info.members)}): {', '.join(sorted(info.members))}")
        return 0

    findings = []
    findings += check_object_initialisers(files, types)
    findings += check_member_access(files, types)
    findings += check_typed_member_access(files, types)
    findings += check_interface_implementation(files, types)

    if not findings:
        print("no contract mismatches detected (heuristic check only - not a compiler)")
        return 0

    print(f"\n{len(findings)} finding(s):")
    for item in sorted(set(findings)):
        print("  -", item)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
