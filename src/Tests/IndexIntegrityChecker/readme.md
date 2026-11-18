# IndexIntegrityChecker

English | [Magyar](readme-hu.md) | [Developer documentation](readme-for-dev.md)

Experimental command-line tool for checking a sensenet **SQL Server database and Lucene 2.9 index**, then repairing the index after manual confirmation. It runs without starting the sensenet repository. SQL is read-only; repair changes the selected index in place.

## Before running

- Install the .NET 8 runtime and keep the complete `publish` directory together. Persistent runs (`-w`) require Windows. Build instructions are in the developer documentation.
- Provide SQL read access, index read access, and writable temporary/working storage with enough space for database and index snapshots and reports. Repair also needs write access to the index.
- Back up the index yourself. Keep SQL and the selected index unchanged by other processes from the start of checking through the end of repair, including pauses between runs. Stop the processes that write to them; the tool does not create an index backup.
- Keep both sources available during repair preparation and confirmation. SQL reads may hold locks and block writers.

## Run a check

From the tool directory, start a run that retains all results and supports resuming:

```powershell
.\publish\IndexIntegrityChecker.exe -d "Server=localhost;Database=sensenet;Integrated Security=true;Encrypt=true" -i "D:\Indexes\LocalIndex" -w "D:\Checks\check-001"
```

Replace the connection string and paths. Each new check needs a new working directory; it is created if missing. For a local development SQL Server with a self-signed certificate, add `TrustServerCertificate=true` if appropriate.

| Parameter | Aliases | Meaning |
| --- | --- | --- |
| `--connection-string` | `--database`, `--db`, `-d` | SQL connection string; required for a new check. |
| `--index` | `-i` | Index directory; required for a new check. |
| `--working-directory` | `--workdir`, `-w` | Retain results and enable continuation before repair starts. |
| `--max-console-differences` | — | Display limit without `-w`: 1–2147483647, default 20. Cannot be combined with `-w`. |

Options can appear in any order; names are case-insensitive, while values retain their original case. Quote values and specify each option only once, including aliases and different letter casing. Both `--option "value"` and `--option="value"` work. Relative paths are resolved from the launch directory.

The index path may contain `segments_*` files directly or point to their parent directory. For a parent, the valid index in the immediate subdirectory that sorts last by name is selected. Use the exact directory when choosing a particular backup.

**Without `-w`**, the run uses temporary files, displays the first 20 differences and the complete summary, then removes its files. It cannot be resumed. Add `--max-console-differences 50` to change the display limit. Redirecting stdout does not remove this limit; use `-w` to retain every difference.

## Confirm or decline repair

After comparison, `Preparing index repair...` precedes the repair preview: documents to delete, versions to write and the final activity ID. When work is needed, the tool asks:

```text
Perform the repair? [yes/NO]
```

Enter **`yes` or `y`** to start, ignoring letter case and surrounding whitespace. Every other answer, Enter alone, end of input or non-interactive/redirected stdin declines repair. To check without repairing, decline this prompt.

Repair uses the full comparison report to restore affected versions from SQL indexing data and remove extra documents. It also sets the index's last activity ID to the database value and clears the gap list. Activity metadata alone can require repair even when the comparison reports no document differences.

With `-w`, declining leaves a prepared workflow that can be continued later. After a successful repair, run a **new check in a new working directory** to verify the result.

## Resume and handle interruptions

Resume using only the same working directory:

```powershell
.\publish\IndexIntegrityChecker.exe -w "D:\Checks\check-001"
```

- Before repair starts, completed phases are reused; an interrupted phase restarts. A prepared repair is checked again and requires confirmation again. SQL and the index must have remained unchanged during the pause.
- **After `RepairStarted`, repair cannot resume.** If interrupted or failed, start a new full check in a new working directory, even if the index may already have been partly or fully modified.
- Reopening a completed workflow displays its saved result; it does not perform a new check or repeat repair. A message saying only the completed result's display failed leaves that completion valid; fix source access and reopen it.

Keep the entire working directory intact: saved files are verified, and missing or changed files cause an error. Source access on resume requires the same Windows user and machine because the saved connection string is encrypted with DPAPI. Source paths remain absolute. Only one process may use a working directory at a time.

## Read the result

`COMPARISON SUMMARY` shows version/document counts, total `differences` and `differentDocuments`. Several errors in one document count as several differences but one affected document; each SQL version missing from the index also counts as an affected document. `COUNTS BY KIND` appears only when differences exist.

| Difference | Meaning |
| --- | --- |
| `NotInIndex` / `NotInDatabase` | Missing index document / document absent from the SQL versions eligible for indexing. |
| `MoreDocument` | An additional document with the same version ID. |
| `DifferentNodeId` | Incorrect node ID. |
| `DifferentNodeTimestamp` / `DifferentVersionTimestamp` | Incorrect node/version timestamp. |
| `DifferentLastPublicFlag` / `DifferentLastDraftFlag` | Incorrect latest public/draft version flag. |
| `MissingField` / `InvalidField` / `MultipleFieldValues` | A required field is missing, invalid or has multiple distinct indexed values. |

`LAST ACTIVITY` always shows the database and index activity IDs and any index gaps. In `COMPARISON SUMMARY`, these are the real values recorded with the compared snapshots, unchanged on reopening or after repair. At most five gap IDs are displayed; `... N total` gives the full count. The saved report also stores only five IDs and the total count.

After repair, `REPAIR SUMMARY` shows deleted documents, written versions and freshly read activity values. Displaying this repair result requires SQL and index access. The comparison summary still describes the state before repair.

With `-w`, `Report: ...` identifies the complete `comparison-<id>.jsonl` file (sometimes named `comparison-<id>-summary.jsonl`). It contains differences, a summary and, after successful repair, a `repair_completed` record. Keep the working directory to retain it. Snapshot/report files are not encrypted. Progress and errors go to stderr; readable summaries go to stdout.

| Exit code | Meaning |
| --- | --- |
| `0` | No repair needed, or confirmed repair completed. |
| `1` | Differences or activity correction remain; repair was not confirmed. |
| `2` | Invalid arguments or a checking, preparation, repair, display or file error. |
| `130` | Cancelled with Ctrl+C. |

In PowerShell, read the exit code with `$LASTEXITCODE`.

## Scope and troubleshooting

The tool checks indexed metadata and version presence. Matching timestamps are accepted as evidence that business fields are correct; it does not compare every business field. Historical versions may retain an older node timestamp when their path matches the current draft. Types with indexing disabled are excluded from the expected SQL versions.

If sources changed, the index is locked, or saved files are damaged, resolve the cause; changed sources require a new full check. Do not edit checkpoint files or force an index unlock. Missing/invalid SQL indexing data, externally stored content-type definitions or unsupported custom indexing settings stop the operation before repair writes; use the reported error and the [developer documentation](readme-for-dev.md) to investigate. If an older workflow has no saved activity state, start a new full check.
