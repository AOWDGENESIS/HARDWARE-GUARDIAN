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
  5. checks that each `class X : IFoo` implements every member of the interface IFoo

Exit code 1 when findings exist. Run:  python3 tools/check-contracts.py
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"

DECL = re.compile(
    r"^\s*(?:public|internal|private|protected)?\s*"
    r"(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|ref\s+)*"
    r"(?P<kind>class|record|struct|interface|enum)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)"
    r"(?P<rest>[^{;]*)",
    re.M,
)

MEMBER = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?:public|internal|protected|private)\s+"
    r"(?:(?:static|virtual|abstract|override|readonly|sealed|partial|async|required|new|extern|unsafe|const)\s+)*"
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

            if kind == "enum":
                body = body_of(text, decl.end())
                info.members.update(re.findall(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*[^,]+)?,", body, re.M))
                info.members.update(re.findall(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*[^,\n]+)?$", body, re.M))
            else:
                body = body_of(text, decl.end())
                for member in MEMBER.finditer(body):
                    info.members.add(member.group("name"))
                for method in METHOD.finditer(body):
                    info.members.add(method.group("name"))
                for method in METHOD_FALLBACK.finditer(body):
                    info.members.add(method.group("name"))
                # records: positional parameters are properties as well
                params = re.search(r"\(([^)]*)\)", rest)
                if params and kind == "record":
                    for part in params.group(1).split(","):
                        token = part.strip().split()[-1].split("=")[0].strip() if part.strip() else ""
                        if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", token or ""):
                            info.members.add(token)

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
