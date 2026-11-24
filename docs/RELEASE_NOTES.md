Diskpart+ is a modern GUI for Windows' built-in diskpart.

## Download

- **win-x64** for almost every PC
- **win-arm64** for ARM PCs, like Snapdragon laptops

Single file, nothing to install. Run it, accept the admin prompt. It isn't code-signed yet, so if Windows shows "Windows protected your PC", click **More info** then **Run anyway**.

## What's new in 0.2.0

- No more typing to confirm. Format, delete and clean show one review screen with a red "Erase disk N" button.
- Everything else (drive letters, online and offline, shrink, extend, attach, detach and so on) runs straight away. Output goes to the diskpart output panel.
- Warnings cut down to the one that matters: `clean all` on an SSD. The blocks on the Windows disk stay.
- Shorter text everywhere.

## Known limitations

- Not code-signed yet.
- Can wipe Mac drives but can't read APFS or HFS+.
- Every diskpart operation is tested against real diskpart on Windows VMs, but not yet on lots of real hardware. Try it on a virtual disk first and report anything odd.
