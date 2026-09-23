#!/usr/bin/env python3
"""Generates a genuine Windows PE32+ (x64) Setup.exe for Windows Maintenance Center.

When executed on Windows, Setup.exe:
  1. Shows a native Windows GUI dialog (MessageBoxW via user32.dll) with product details.
  2. If the user clicks 'Ja', executes Setup.cmd via ShellExecuteW (shell32.dll)
     to run the automated build and installation pipeline.
  3. Exits cleanly via ExitProcess (kernel32.dll).

This executable is 100% genuine PE32+ x64 machine code and headers, verified by pefile.
"""

from __future__ import annotations

import struct
from pathlib import Path

try:
    import pefile
except ImportError:
    pefile = None


def generate_setup_exe(output_path: Path) -> bytes:
    # --------------------------------------------------------------------------
    # DOS Header + DOS Stub (128 bytes)
    # --------------------------------------------------------------------------
    dos_header = bytearray(64)
    dos_header[0:2] = b"MZ"
    struct.pack_into("<I", dos_header, 0x3C, 0x80)  # e_lfanew = 128 (0x80)

    dos_stub = b"\x0e\x1f\xba\x0e\x00\xb4\x09\xcd\x21\xb8\x01\x4c\xcd\x21This program cannot be run in DOS mode.\r\r\n$\x00\x00\x00\x00\x00\x00\x00".ljust(64, b"\x00")

    # --------------------------------------------------------------------------
    # PE Signature + COFF File Header (24 bytes)
    # --------------------------------------------------------------------------
    pe_sig = b"PE\x00\x00"
    file_header = struct.pack(
        "<HHIIIHH",
        0x8664,      # Machine: IMAGE_FILE_MACHINE_AMD64 (x64)
        2,           # NumberOfSections: .text, .rdata
        0x67000000,  # TimeDateStamp
        0, 0,        # PointerToSymbolTable, NumberOfSymbols
        240,         # SizeOfOptionalHeader
        0x0022       # Characteristics: EXECUTABLE_IMAGE | LARGE_ADDRESS_AWARE
    )

    # --------------------------------------------------------------------------
    # Optional Header (PE32+, 240 bytes)
    # --------------------------------------------------------------------------
    opt_header = bytearray(240)
    fmt = "<HBBIIII I QII HHHHHH IIII HH QQQQ II"
    items = [
        0x020B,      # Magic: PE32+ (64-bit)
        14, 0,       # MajorLinkerVersion, MinorLinkerVersion
        0x200,       # SizeOfCode (.text raw size)
        0x600,       # SizeOfInitializedData (.rdata raw size)
        0,           # SizeOfUninitializedData
        0x1000,      # AddressOfEntryPoint (RVA 0x1000 = start of .text)
        0x1000,      # BaseOfCode
        0x140000000, # ImageBase
        0x1000,      # SectionAlignment
        0x200,       # FileAlignment
        6, 0,        # MajorOperatingSystemVersion, MinorOperatingSystemVersion (Windows 6.0+)
        1, 0,        # MajorImageVersion, MinorImageVersion
        6, 0,        # MajorSubsystemVersion, MinorSubsystemVersion
        0,           # Win32VersionValue
        0x3000,      # SizeOfImage (Headers 0x1000 + .text 0x1000 + .rdata 0x1000)
        0x200,       # SizeOfHeaders
        0,           # CheckSum
        2,           # Subsystem: IMAGE_SUBSYSTEM_WINDOWS_GUI (native GUI, no console popup)
        0x8160,      # DllCharacteristics: DYNAMIC_BASE | NX_COMPAT | HIGH_ENTROPY_VA | TERMINAL_SERVER_AWARE
        0x100000, 0x1000, # SizeOfStackReserve, SizeOfStackCommit
        0x100000, 0x1000, # SizeOfHeapReserve, SizeOfHeapCommit
        0,           # LoaderFlags
        16           # NumberOfRvaAndSizes
    ]
    struct.pack_into(fmt, opt_header, 0, *items)

    # Data Directory 1: Import Table (RVA 0x2000, Size 80 bytes)
    struct.pack_into("<II", opt_header, 112 + 1 * 8, 0x2000, 80)
    # Data Directory 12: IAT (RVA 0x2090, Size 48 bytes)
    struct.pack_into("<II", opt_header, 112 + 12 * 8, 0x2090, 48)

    # --------------------------------------------------------------------------
    # Section Headers (2 * 40 = 80 bytes)
    # --------------------------------------------------------------------------
    sec_text = struct.pack(
        "<8sIIIIIIHHI",
        b".text\x00\x00\x00",
        0x200, 0x1000, # VirtualSize, VirtualAddress
        0x200, 0x200,  # SizeOfRawData, PointerToRawData
        0, 0, 0, 0,
        0x60000020     # IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ
    )

    sec_rdata = struct.pack(
        "<8sIIIIIIHHI",
        b".rdata\x00\x00",
        0x600, 0x2000, # VirtualSize, VirtualAddress
        0x600, 0x400,  # SizeOfRawData, PointerToRawData
        0, 0, 0, 0,
        0x40000040     # IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ
    )

    headers = (dos_header + dos_stub + pe_sig + file_header + opt_header + sec_text + sec_rdata).ljust(0x200, b"\x00")

    # --------------------------------------------------------------------------
    # .rdata Section (Imports, Tables, Strings)
    # --------------------------------------------------------------------------
    rdata = bytearray(0x600)
    def rdata_offset(rva: int) -> int:
        return rva - 0x2000

    # Import Directory (4 entries * 20 bytes = 80 bytes)
    # Entry 0: kernel32.dll
    struct.pack_into("<IIIII", rdata, 0, 0x2050, 0, 0, 0x2120, 0x2090)
    # Entry 1: user32.dll
    struct.pack_into("<IIIII", rdata, 20, 0x2060, 0, 0, 0x2130, 0x20A0)
    # Entry 2: shell32.dll
    struct.pack_into("<IIIII", rdata, 40, 0x2070, 0, 0, 0x2140, 0x20B0)
    # Entry 3: NULL terminator (already zeros)

    # ILT (Import Lookup Tables)
    struct.pack_into("<QQ", rdata, rdata_offset(0x2050), 0x20D0, 0) # kernel32!ExitProcess
    struct.pack_into("<QQ", rdata, rdata_offset(0x2060), 0x20E0, 0) # user32!MessageBoxW
    struct.pack_into("<QQ", rdata, rdata_offset(0x2070), 0x20F0, 0) # shell32!ShellExecuteW

    # IAT (Import Address Tables)
    struct.pack_into("<QQ", rdata, rdata_offset(0x2090), 0x20D0, 0)
    struct.pack_into("<QQ", rdata, rdata_offset(0x20A0), 0x20E0, 0)
    struct.pack_into("<QQ", rdata, rdata_offset(0x20B0), 0x20F0, 0)

    # Hint/Name entries
    def put_hint(rva: int, name: str) -> None:
        off = rdata_offset(rva)
        rdata[off:off+2] = b"\x00\x00"
        bname = name.encode("ascii") + b"\x00"
        rdata[off+2:off+2+len(bname)] = bname

    put_hint(0x20D0, "ExitProcess")
    put_hint(0x20E0, "MessageBoxW")
    put_hint(0x20F0, "ShellExecuteW")

    # DLL Names
    def put_str(rva: int, s: str) -> None:
        off = rdata_offset(rva)
        b = s.encode("ascii") + b"\x00"
        rdata[off:off+len(b)] = b

    put_str(0x2120, "kernel32.dll")
    put_str(0x2130, "user32.dll")
    put_str(0x2140, "shell32.dll")

    # Strings (UTF-16LE)
    title = "Windows Maintenance Center - Setup".encode("utf-16le") + b"\x00\x00"
    msg = (
        "Windows Maintenance Center v1.0.0\n"
        "Windows Hardware Diagnostics, Maintenance & Update Center\n\n"
        "Dieser Setup-Starter führt Sie durch die Einrichtung und den Bau des Release-Pakets.\n\n"
        "Möchten Sie jetzt den Build- und Installationsassistenten (Setup.cmd / PowerShell) starten?"
    ).encode("utf-16le") + b"\x00\x00"
    op = "open".encode("utf-16le") + b"\x00\x00"
    file = "Setup.cmd".encode("utf-16le") + b"\x00\x00"

    rva_title = 0x2160
    rva_msg = rva_title + len(title)
    rva_op = rva_msg + len(msg)
    rva_file = rva_op + len(op)

    rdata[rdata_offset(rva_title):rdata_offset(rva_title)+len(title)] = title
    rdata[rdata_offset(rva_msg):rdata_offset(rva_msg)+len(msg)] = msg
    rdata[rdata_offset(rva_op):rdata_offset(rva_op)+len(op)] = op
    rdata[rdata_offset(rva_file):rdata_offset(rva_file)+len(file)] = file

    # --------------------------------------------------------------------------
    # .text Section (x86_64 Machine Code)
    # --------------------------------------------------------------------------
    code = bytearray(0x200)

    def emit_rel32(curr_rip: int, target_rva: int) -> bytes:
        return struct.pack("<i", target_rva - curr_rip)

    p = 0
    # sub rsp, 40 (48 83 ec 28)
    code[p:p+4] = b"\x48\x83\xec\x28"; p += 4

    # xor ecx, ecx (31 c9)
    code[p:p+2] = b"\x31\xc9"; p += 2

    # lea rdx, [rip + msg] (48 8d 15 xx xx xx xx)
    code[p:p+3] = b"\x48\x8d\x15"; p += 3
    code[p:p+4] = emit_rel32(0x1000 + p + 4, rva_msg); p += 4

    # lea r8, [rip + title] (4c 8d 05 xx xx xx xx)
    code[p:p+3] = b"\x4c\x8d\x05"; p += 3
    code[p:p+4] = emit_rel32(0x1000 + p + 4, rva_title); p += 4

    # mov r9d, 0x44 (MB_YESNO | MB_ICONINFORMATION) (41 b9 44 00 00 00)
    code[p:p+6] = b"\x41\xb9\x44\x00\x00\x00"; p += 6

    # call [rip + iat_MessageBoxW (0x20A0)] (ff 15 xx xx xx xx)
    code[p:p+2] = b"\xff\x15"; p += 2
    code[p:p+4] = emit_rel32(0x1000 + p + 4, 0x20A0); p += 4

    # cmp eax, 6 (IDYES) (83 f8 06)
    code[p:p+3] = b"\x83\xf8\x06"; p += 3

    # jne do_exit (75 2a -> jumps 42 bytes over ShellExecute)
    code[p:p+2] = b"\x75\x2a"; p += 2

    # --- IF YES: ShellExecuteW(NULL, L"open", L"Setup.cmd", NULL, NULL, SW_SHOWNORMAL) ---
    # xor ecx, ecx (31 c9)
    code[p:p+2] = b"\x31\xc9"; p += 2

    # lea rdx, [rip + op] (48 8d 15 xx xx xx xx)
    code[p:p+3] = b"\x48\x8d\x15"; p += 3
    code[p:p+4] = emit_rel32(0x1000 + p + 4, rva_op); p += 4

    # lea r8, [rip + file] (4c 8d 05 xx xx xx xx)
    code[p:p+3] = b"\x4c\x8d\x05"; p += 3
    code[p:p+4] = emit_rel32(0x1000 + p + 4, rva_file); p += 4

    # xor r9d, r9d (45 31 c9)
    code[p:p+3] = b"\x45\x31\xc9"; p += 3

    # mov qword [rsp + 32], 0 (48 c7 44 24 20 00 00 00 00)
    code[p:p+9] = b"\x48\xc7\x44\x24\x20\x00\x00\x00\x00"; p += 9

    # mov dword [rsp + 40], 1 (SW_SHOWNORMAL) (c7 44 24 28 01 00 00 00)
    code[p:p+8] = b"\xc7\x44\x24\x28\x01\x00\x00\x00"; p += 8

    # call [rip + iat_ShellExecuteW (0x20B0)] (ff 15 xx xx xx xx)
    code[p:p+2] = b"\xff\x15"; p += 2
    code[p:p+4] = emit_rel32(0x1000 + p + 4, 0x20B0); p += 4

    # --- do_exit: ExitProcess(0) ---
    # xor ecx, ecx (31 c9)
    code[p:p+2] = b"\x31\xc9"; p += 2

    # call [rip + iat_ExitProcess (0x2090)] (ff 15 xx xx xx xx)
    code[p:p+2] = b"\xff\x15"; p += 2
    code[p:p+4] = emit_rel32(0x1000 + p + 4, 0x2090); p += 4

    # ret (c3)
    code[p:p+1] = b"\xc3"; p += 1

    pe_bytes = headers + bytes(code) + bytes(rdata)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(pe_bytes)

    if pefile:
        pe = pefile.PE(data=pe_bytes)
        print(f"Verified PE {output_path.name}: machine={hex(pe.FILE_HEADER.Machine)}, subsystem={pe.OPTIONAL_HEADER.Subsystem}, imports={len(pe.DIRECTORY_ENTRY_IMPORT)}")

    return pe_bytes


if __name__ == "__main__":
    import sys
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("Setup.exe")
    data = generate_setup_exe(out)
    print(f"Successfully generated {out} ({len(data)} bytes)")
