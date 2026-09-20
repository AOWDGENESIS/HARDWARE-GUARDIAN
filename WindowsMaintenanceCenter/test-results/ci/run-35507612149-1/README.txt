Windows Maintenance Center
Windows Hardware Diagnostics, Maintenance & Update Center
Version 1.0.0 - Copyright (c) AOWDGENESIS

What it is
----------
A local Windows tool that reads your hardware, checks the health of drivers, storage
and Windows itself, and offers maintenance and update tasks. It runs completely on
your machine: no cloud, no account, no telemetry (telemetry is fixed to off).

How to start
------------
Double-click WindowsMaintenanceCenter.exe. The application starts, scans the machine and
shows what it found. Every value carries its source; a value that could not be
measured is shown as UNKNOWN with the reason, never as a zero or an estimate.

What it does NOT do
-------------------
  * it never flashes a BIOS or firmware, it only tells you which file would fit
    and where the official source is
  * it never installs drivers or updates without your explicit approval
  * it never uses third-party driver portals; only official vendor, Microsoft and
    Windows Update sources are accepted
  * it never disables your anti-virus, never bypasses UAC, and never repairs
    Windows without asking first
  * it does not read passwords, cookies or browser sessions

Where your data stays
---------------------
Portable mode (this folder): everything below the sub folder "data".
Installed mode: %ProgramData%\WindowsMaintenanceCenter (machine wide) and
%LocalAppData%\WindowsMaintenanceCenter (per user).
The marker file "WindowsMaintenanceCenter.portable" next to the executable selects portable
mode. Delete it to use the installed layout. Reports, backups and the audit log are
plain files on disk; they are never transmitted anywhere.

Safety order
------------
DETECT -> VERIFY -> ANALYZE -> BACKUP -> YOUR APPROVAL -> EXECUTE -> VERIFY -> ROLLBACK

Every deletion is planned first, shown to you as a dry run, and only executed after
you approve it. Maintenance categories are marked SAFE, OPTIONAL, PROTECTED or
UNKNOWN; nothing marked UNKNOWN is ever touched.

Requirements
------------
Windows 10 (1809 or newer) or Windows 11, 64-bit. The build is a single file that
already contains the .NET runtime; nothing else has to be installed.

More
----
See the application's own "About" page for version, commit and build date, and the
docs folder of the source repository for the full specification coverage.
