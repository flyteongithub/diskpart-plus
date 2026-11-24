# Architecture

Diskpart+ is split so the risky part (deciding what to run) can be tested properly, away from Windows and away from the UI.

```
   UI picks what to do
          │
          ▼
   Operations             plain records like FormatPartition(disk 3, partition 1, NTFS)
          │
          ▼
   OperationPlanner       checks every step, works out the risk, builds the script
          │
          ▼
   OperationPlan          issues + risk + confirmation phrase + script + target fingerprints
          │
          │   user reads the plan and types the confirmation phrase
          ▼
   Last-minute check      read the disks again, make sure disk N is still the same drive
          │
          ▼
   DiskpartRunner         diskpart /s, output streamed live
          │
          ▼
   Read the disks again   confirm the change actually happened
```

## Projects

| Project | What it does | Depends on |
|---|---|---|
| `DiskpartPlus.Core` | Models, partition types, operations, safety rules, planner, script builder | nothing |
| `DiskpartPlus.Windows` *(next)* | Reads disks through the Windows storage API and runs diskpart | Core |
| `DiskpartPlus.App` *(next)* | The WPF interface | Core, Windows |

Core has no dependencies and doesn't call any Windows APIs. That's on purpose: every rule about what's safe to run can be unit tested on any machine, including CI.

## Reading disks

The plan is to read disk state from the Windows storage management API (`MSFT_Disk`, `MSFT_Partition`, `MSFT_Volume` and `MSFT_PhysicalDisk` over CIM) and not from diskpart's own output.

diskpart's output is translated into whatever language Windows is set to, so anything that matches English words breaks for most of the world. It also rounds sizes and is slow to start. The storage API gives structured data with exact byte sizes in every language.

Diskpart+ only reads diskpart's output to show it to the user. It never uses it to decide what happened.

## Changing disks

All changes go through diskpart, so the tool stays transparent: what you see in the preview is exactly what runs.

The UI never writes diskpart text. It builds `DiskOperation` records and `DiskpartScriptBuilder` turns them into commands. That builder is the only place in the codebase with diskpart syntax in it.

A few rules the builder sticks to:

- **Every operation selects its own target.** Even if the step before already selected disk 2, the next step selects it again. It's a few extra lines, but no step can act on whatever happened to be selected last.
- **No `noerr`, ever.** diskpart stops at the first error, so nothing runs after something goes wrong.
- **Quoted values are checked.** Labels and file paths can't contain quotes or line breaks, so nothing typed into a text box can inject an extra command.
- **Every step gets a `rem` comment** so the raw script is readable.

## Planning

`OperationPlanner.Create` takes a list of operations and a snapshot of the disks. It walks the steps in order and keeps track of what earlier steps will have done by then. After a `clean`, for example, the old partitions are gone and the disk is RAW, so converting to GPT is fine but formatting "partition 2" isn't.

Each step goes through two checks:

- **`OperationValidator`** asks "will diskpart accept this?" FAT32 over 32 GB, labels too long for the file system, a drive letter already in use, converting a disk that still has partitions and so on.
- **`SafetyPolicy`** asks "is this a good idea?" It blocks anything destructive on critical disks and warns about things like BitLocker volumes, mounted drives or running `clean all` on an SSD.

Every issue has a stable code (see `IssueCodes`) so tests and translations don't depend on message text.

### Risk levels

| Level | Examples | Confirmation |
|---|---|---|
| Safe | Assign a letter, attach a VHD | none |
| Caution | Shrink, take a disk offline, convert an empty disk | none |
| Destructive | Format, delete a partition, `clean` | type `ERASE DISK n` |
| Critical | `clean all` | type `WIPE DISK n` |

### What counts as critical

A disk is critical if it holds the running copy of Windows, the system (EFI) partition the PC booted from, the page file or Diskpart+ itself. Destructive changes to a critical disk are blocked with no way round it. Partitions on a critical disk that aren't critical themselves (a D: drive on the same SSD as C:, say) are allowed with a warning.

### Same drive check

Disk numbers aren't stable. Unplug one USB drive and plug in another and "disk 2" can be a different drive. diskpart can only select disks by number, so every plan records a fingerprint of each disk it touches (number, size, unique ID, serial and name). Right before running, the app reads the disks again and `CheckTargetsStillMatch` refuses to go ahead if anything has changed.

## Running scripts *(next)*

- Each run writes its script to a fresh temp folder with a unique name and deletes it afterwards.
- `diskpart /s` runs with stdout streamed line by line into the UI.
- Success is judged by the exit code and then by reading the disks again, not by parsing text.
- Only one diskpart run at a time. Microsoft's docs say to leave at least 15 seconds between back-to-back scripts so the previous one has fully shut down, so the runner enforces a short cooldown.
- Most operations can't be cancelled once started. Killing diskpart halfway through a format or clean can leave a disk half written, so the UI only offers an abort for `clean all`, which can take hours.

## Elevation

diskpart needs administrator rights, so the app's manifest asks for them up front (`requireAdministrator`). An unelevated UI talking to a separate elevated helper would be cleaner, but it doubles the plumbing and isn't worth it for v1.

One side effect: Windows blocks drag and drop from a normal Explorer window into an elevated app, so VHD paths are picked with a file dialog.
