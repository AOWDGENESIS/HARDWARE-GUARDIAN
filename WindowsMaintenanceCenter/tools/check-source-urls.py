#!/usr/bin/env python3
"""Checks the manufacturer sources in ManufacturerSources.cs against reality.

The update engine may only use official vendor sources (spec sections 12, 30, 47, 48). The
source list therefore carries a claim per entry: either "reachability verified <date>" or
"reachability was not verified". This tool keeps that claim honest.

It does two things that cannot be done offline:

  1. Every landing URL is requested over HTTPS and the result is reported per source.
  2. The result is compared with the claim in the source file. A source that claims
     VerificationLevel.SourceReachable must really answer; a source marked NotVerified that
     does answer is reported as a hint so the marker can be raised.

Offline parts (always run, no network needed):
  - only HTTPS, no credentials in the URL, no IP literal, no localhost
  - no known third-party driver portal is allowed in this file at all (rule 12)

Exit codes:
  0  every source matches its claim
  1  at least one source contradicts its claim, or a policy rule was broken
  2  the tool itself failed
  3  the network part did not run (no outbound network, or a proxy was not reachable) - this is
     not a pass

A proxy is honoured through the usual https_proxy/https_proxy environment variables.

Usage:  python3 tools/check-source-urls.py [--timeout 12] [--quiet] [--offline]

--offline runs the policy part only (no requests). It is meant for environments without a
network and for the mutation self-test, and it never claims that a URL was verified.
"""

from __future__ import annotations

import argparse
import ipaddress
import re
import socket
import ssl
import sys
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE_FILE = ROOT / "src" / "WindowsMaintenanceCenter.Manufacturer" / "ManufacturerSources.cs"

# Domains that are third-party driver portals. The spec forbids them as update sources
# (rule 12); they must not even appear in the source list.
FORBIDDEN_HOSTS = (
    "driveridentifier.com",
    "driverguide.com",
    "driveragent.com",
    "drivermax.com",
    "snappy-driver-installer.org",
    "touslesdrivers.com",
    "majorgeeks.com",
    "softpedia.com",
    "filehorse.com",
    "filehippo.com",
    "cnet.com",
    "download.com",
)

URL_RE = re.compile(r'LandingUrl\s*=\s*(?:Unknown|"([^"]+)")')
CLAIM_RE = re.compile(
    r"BaselineVerification\s*=\s*VerificationLevel\.(?P<level>\w+),(?P<rest>[^\n]*)", re.M
)

USER_AGENT = "WindowsMaintenanceCenter-SourceCheck/1.0 (+offline-first diagnostics tool; URL reachability only)"


class Source:
    def __init__(self, index: int, url: str, claimed_reachable: bool, claim_line: str, block: str):
        self.index = index
        self.url = url
        self.claimed_reachable = claimed_reachable
        self.claim_line = claim_line
        self.block = block

    @property
    def host(self) -> str:
        return urllib.parse.urlsplit(self.url).hostname or ""

    @property
    def name(self) -> str:
        match = re.search(r'DisplayNameKey\s*=\s*"([^"]+)"', self.block)
        return match.group(1) if match else f"source #{self.index}"


def parse_sources(text: str) -> list[Source]:
    """Splits the file into property blocks so a URL and its claim stay together."""
    blocks: list[tuple[int, int]] = []
    for match in re.finditer(r"public static ManufacturerSource \w+ \{ get; \} = new\(\)", text):
        end = text.find("\n    };", match.end())
        blocks.append((match.start(), end if end != -1 else len(text)))

    sources: list[Source] = []
    index = 0
    for start, end in blocks:
        block = text[start:end]
        url_match = URL_RE.search(block)
        if not url_match:
            continue
        index += 1
        url = url_match.group(1) or ""
        claim = CLAIM_RE.search(block)
        level = claim.group("level") if claim else "NotVerified"
        sources.append(
            Source(
                index=index,
                url=url,
                claimed_reachable=level == "SourceReachable",
                claim_line=claim.group(0).strip() if claim else "",
                block=block,
            )
        )
    return sources


def offline_findings(source: Source) -> list[str]:
    """Policy rules that need no network. These mirror SourceVerifier.IsAcceptableUrl."""
    findings: list[str] = []
    if not source.url:
        # An empty URL is allowed: it means SOURCE UNKNOWN and the UI says so.
        return findings

    parts = urllib.parse.urlsplit(source.url)
    if parts.scheme != "https":
        findings.append("only HTTPS sources are accepted")
    if parts.username or parts.password:
        findings.append("URL must not contain credentials")
    if parts.hostname in ("localhost",):
        findings.append("localhost is not a valid manufacturer source")
    try:
        address = ipaddress.ip_address(parts.hostname or "")
    except ValueError:
        pass
    else:
        if address.is_loopback or address.is_private or address.is_link_local:
            findings.append("local or private addresses are not valid manufacturer sources")
        else:
            findings.append("sources must be named hosts, not IP addresses")
    for forbidden in FORBIDDEN_HOSTS:
        if source.host == forbidden or source.host.endswith("." + forbidden):
            findings.append(f"third-party portal {forbidden} is never an accepted update source")
    return findings


def probe(url: str, timeout: float) -> tuple[bool, str]:
    """Requests the URL once. Returns (reachable, description)."""
    request = urllib.request.Request(url, method="GET", headers={"User-Agent": USER_AGENT})
    context = ssl.create_default_context()
    try:
        with urllib.request.urlopen(request, timeout=timeout, context=context) as response:
            status = getattr(response, "status", None) or response.getcode()
            # Reading a little proves the TLS handshake and the response really happened.
            response.read(256)
            return 200 <= int(status) < 400, f"HTTP {status}"
    except urllib.error.HTTPError as error:
        # An HTTP error still proves that the host answers over HTTPS (some vendors send 403
        # to non-browser clients). Report it as reachable but name the status.
        return True, f"HTTP {error.code} (host answered)"
    except urllib.error.URLError as error:
        reason = error.reason
        if isinstance(reason, socket.gaierror):
            return False, f"DNS failure ({reason})"
        if isinstance(reason, ssl.SSLError):
            return False, f"TLS failure ({reason})"
        if isinstance(reason, (TimeoutError, socket.timeout)):
            return False, f"timeout after {timeout:g}s"
        return False, f"not reachable ({reason})"
    except (TimeoutError, socket.timeout):
        return False, f"timeout after {timeout:g}s"
    except OSError as error:
        return False, f"network error ({error})"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--timeout", type=float, default=12.0, help="seconds per request")
    parser.add_argument("--quiet", action="store_true", help="only report findings")
    parser.add_argument(
        "--offline",
        action="store_true",
        help="policy checks only, no HTTP requests (the network part is reported as not run)",
    )
    args = parser.parse_args()

    if not SOURCE_FILE.is_file():
        print(f"error: {SOURCE_FILE} is missing", file=sys.stderr)
        return 2

    sources = parse_sources(SOURCE_FILE.read_text(encoding="utf-8"))
    if not sources:
        print("error: no sources were parsed - the file format changed", file=sys.stderr)
        return 2

    print(f"=== Manufacturer sources ({len(sources)} entries in {SOURCE_FILE.name}) ===")

    findings: list[str] = []      # policy findings: real regardless of the network
    claim_findings: list[str] = []  # only meaningful once the network part really ran
    for source in sources:
        for finding in offline_findings(source):
            findings.append(f"{source.name}: {finding}")
        if not source.url:
            print(f"  {source.name:<26} SOURCE UNKNOWN (no URL, as documented)")

    configured = [s for s in sources if s.url]
    print(f"  {len(configured)} of {len(sources)} sources carry a URL")

    reachable_count = 0
    hints: list[str] = []
    stopped_early = False
    failures: list[str] = []
    for source in configured:
        if args.offline:
            stopped_early = True
            break
        if len(failures) >= 3 and reachable_count == 0:
            # Three dead sources in a row and not a single answer: this is a blocked egress, not
            # eighteen broken vendor pages. Stop instead of spending a timeout per remaining entry.
            stopped_early = True
            break
        reachable, description = probe(source.url, args.timeout)
        if reachable:
            reachable_count += 1
        else:
            failures.append(f"{source.name}: {description}")
        if reachable and not source.claimed_reachable:
            hints.append(
                f"{source.name}: reachable ({description}) but marked NotVerified - the marker can be raised"
            )
        if not reachable and source.claimed_reachable:
            claim_findings.append(
                f"{source.name}: claims VerificationLevel.SourceReachable but is not reachable ({description})"
            )
        if not args.quiet:
            claim = "claims reachable" if source.claimed_reachable else "claims not verified"
            state = "reachable" if reachable else "NOT reachable"
            print(f"  {source.name:<26} {source.url}\n{'':27}{state} ({description}); file {claim}")

    if reachable_count == 0 or stopped_early:
        # Not one source answered. That is the signature of a blocked egress (this build
        # environment has no direct outbound TLS), not of eighteen broken vendor pages, so the
        # network part is reported as "did not run" instead of producing eighteen findings.
        print()
        print("the network part did not run:", end=" ")
        if args.offline:
            print("--offline was requested, so no request was sent")
        elif reachable_count == 0 and failures:
            print("not one of the sources answered, which means this environment has no direct")
            print("outbound network (a proxy can be given via https_proxy)")
        else:
            print("no complete round was possible")
        print("this is NOT a pass - the URL claims in the source file remain unverified here")
        for failure in failures:
            print(f"  tried {failure}")
        if findings:
            print()
            print("offline policy findings:")
            for finding in findings:
                print(f"  - {finding}")
            return 1
        return 3

    print()
    for hint in hints:
        print(f"  hint: {hint}")

    findings.extend(claim_findings)
    if findings:
        print()
        print(f"{len(findings)} finding(s):")
        for finding in findings:
            print(f"  - {finding}")
        return 1

    print(f"every one of the {len(configured)} configured sources matches its claim")
    return 0


if __name__ == "__main__":
    sys.exit(main())
