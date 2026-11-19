# IndexIntegrityChecker — developer documentation

English | [Magyar](readme-hu-for-dev.md) | [User guide](readme.md)

A standalone experimental console tool for comparing a sensenet SQL Server database with a Lucene 2.9 index and repairing the index after manual confirmation. It processes snapshots in a working directory. By default, the directory is temporary and removed after the run; specifying `-w` retains checkpoints and enables resuming before repair starts. SQL is always read-only; confirmed repair modifies the selected index in place.

## Usage

Start a new check with a connection string and an index path. The working directory is optional. Named parameters can appear in any order:

| Parameter | Primary name | Aliases |
| --- | --- | --- |
| SQL Server connection string | `--connection-string` | `--database`, `--db`, `-d` |
| Lucene index path | `--index` | `-i` |
| Persistent working directory (optional) | `--working-directory` | `--workdir`, `-w` |
| Maximum differences written to stdout (temporary mode only, default 20) | `--max-console-differences` | |

Run without persistence:

```powershell
.\publish\IndexIntegrityChecker.exe --db "Server=localhost;Database=sensenet;Integrated Security=true;Encrypt=true;TrustServerCertificate=true" --index "D:\Indexes\LocalIndex"
```

Retain snapshots, checkpoints, and the report:

```powershell
.\publish\IndexIntegrityChecker.exe --db "Server=localhost;Database=sensenet;Integrated Security=true;Encrypt=true;TrustServerCertificate=true" --index "D:\Indexes\LocalIndex" --workdir "D:\Checks\check-001"
```

Resume the same workflow with only its working directory:

```powershell
.\publish\IndexIntegrityChecker.exe -w "D:\Checks\check-001"
```

Each persistent working directory belongs to one workflow. Use a different directory for a new persistent check. If the workflow has completed, `-w` verifies its saved files and reuses the saved comparison, including its recorded activity state. A completed repair also displays a repair summary with the current SQL and Lucene activity state. At startup, standard error shows the selected mode.

Enclose values in quotes. The `--option=value` form is also supported, for example `--workdir="D:\Checks\check-001"`. Option names are case-insensitive and normalized independently of the current culture; values retain their original case. Specify each parameter once; multiple aliases or different letter casing for the same parameter count as duplicates. Missing values, unknown options, and duplicates produce usage information and exit code 2. Without `-w`, both source parameters are required. With `-w`, creating a workflow requires both sources; resuming an existing workflow requires only `-w`. Source settings supplied again must match the saved settings.

`--max-console-differences N` accepts an integer from 1 to 2147483647 and defaults to 20. It is available only without a persistent working directory. Specifying it with `-w`, `--workdir`, or `--working-directory` is an argument error, including when resuming. The limit counts difference records, so multiple differences in one document count separately.

The example uses `TrustServerCertificate=true` for a local development SQL Server; omit it when the server has a valid certificate. The tool does not log the connection string or password.

The index path can point to the directory containing `segments_*` files or its parent. For a parent directory, the tool selects the immediate subdirectory that contains a valid index and sorts last by name. This supports sensenet's timestamp-named directories. In persistent mode, the resolved directory and commit version are saved in `workflow.json`; resuming uses that same index.

The specified working directory is created if missing. Relative index and working directory paths are resolved against the launch directory; the process's current directory is unchanged. A persistent working directory can be moved after saving, but the captured source index path remains absolute.

## Temporary runs

Omitting `-w` creates a unique directory under `Path.GetTempPath()/IndexIntegrityChecker/<GUID>`. On Windows this is normally under the user's temporary directory. The same streaming exports and disk-backed metadata are used, so temporary runs still require space for the data files and report.

Connection settings and workflow state stay in memory: no `workflow.json` or `.dpapi` file is written. Each invocation starts a new check and requires both sources. Temporary runs cannot be resumed.

The tool removes its generated run directory after success, detected differences, handled errors, or Ctrl+C. The shared temporary parent directory and other runs are left intact. Forced process termination can leave temporary files behind. A cleanup failure reports the remaining path and changes an otherwise successful exit code (0 or 1) to 2; cancellation retains exit code 130.

The first N differences stream to standard output, followed by a readable comparison summary; N defaults to 20. The check continues through all records after reaching the output limit. When the first additional difference is found, standard error displays one notice explaining the limit and how to retain all differences with `-w`. No notice is emitted when there are at most N differences. All summary counts cover the complete check.

Use a custom output limit:

```powershell
.\publish\IndexIntegrityChecker.exe -d "<connectionstring>" -i "D:\Indexes\LocalIndex" --max-console-differences 50
```

The limit also applies when stdout is redirected. This saves at most N differences and the complete summary:

```powershell
.\publish\IndexIntegrityChecker.exe -d "<connectionstring>" -i "D:\Indexes\LocalIndex" > result.txt
```

To retain every difference, start a persistent run:

```powershell
.\publish\IndexIntegrityChecker.exe -d "<connectionstring>" -i "D:\Indexes\LocalIndex" -w "D:\Checks\check-001"
```

## Workflow and resuming

Checkpoint messages use the format `Checkpoint: <state>.` on standard error, with the state shown in green in the console. Resuming uses `Resuming workflow: <state>.`, with the state in yellow. Redirected output contains plain text without color codes.

With `-w`, the `phase` property in `workflow.json` records the last completed checkpoint:

| State | Saved result | Next step on resume |
| --- | --- | --- |
| `Initialized` | Source index identity and encrypted SQL connection settings | Export the database |
| `DatabaseSaved` | Ordered SQL rows | Export the captured index |
| `IndexSaved` | Ordered index rows, field issues, and historical path metadata | Compare the saved files |
| `ComparisonSaved` | Complete report and summary | Prepare repair from the report and live sources |
| `Completed` | Final result | Return the saved result |
| `RepairPrepared` | Complete report and repair preview | Repeat read-only preparation and ask for confirmation |
| `RepairStarted` | Durable marker saved before any index write | Reject continuation; a new full check is required |
| `RepairCompleted` | Successful repair summary | Return the result without applying the report again |

Resuming works at **phase boundaries**. An interrupted database export, index export, or comparison restarts from the beginning of that phase. Completed earlier phases are reused. There are no checkpoints within an export or the comparison.

Database export needs SQL; index export needs the original index. Comparison and its summary use saved snapshots. Repair preparation and execution require both sources and the saved connection settings again. Displaying the comparison summary requires no source access. Displaying the repair summary for `RepairCompleted` requires both sources and the saved connection settings to read the current activity state.

Output files are written with a `.partial` suffix, flushed to disk, and renamed when complete. The state file is replaced atomically after every file for that checkpoint is complete. A failed or cancelled phase preserves the preceding checkpoint. Uncommitted files are ignored and replaced when their phase runs again.

On resume, the tool validates the length and SHA-256 checksum of every file referenced by a completed checkpoint. This requires sequential reads of the saved data, including when returning a completed result. Missing, truncated, or modified files cause an error; they are not silently regenerated from potentially different sources.

The tool holds `workflow.lock` exclusively while running. Two processes cannot use the same workspace concurrently. The file remains after exit, but its operating-system lock is released; it does not need to be deleted.

## Working directory contents

Persistent runs retain the files listed below. Temporary runs use the data, report, lock, and scratch files while executing, then remove their entire run directory; they do not create the state or credentials files.

| File | Contents |
| --- | --- |
| `workflow.json` | Workflow ID, phase, source index identity, file names, row counts, lengths, checksums, and result summary |
| `workflow.lock` | Exclusive workspace lock |
| `connection-<id>.dpapi` | Encrypted SQL connection settings |
| `database-<id>.tsv` | SQL snapshot ordered by `VersionId` |
| `index-<id>.tsv` | Index snapshot ordered by `VersionId`, preserving duplicates |
| `index-issues-<id>.jsonl` | Field issues found during index export |
| `comparison-<id>.jsonl` or `comparison-<id>-summary.jsonl` | Complete differences, comparison summary and successful repair record; the active report path is recorded in `workflow.json` |
| `index-scratch-<id>.map` | Temporary metadata addressed by Lucene DocId |
| `comparison-scratch-<id>.bits` | Temporary tracking of already reported missing draft paths |
| `different-documents-<id>.bits` | Temporary tracking of distinct index documents with reported differences |

In persistent mode, the connection string is encrypted with Windows DPAPI for the current user, with the workflow ID as additional entropy. Database export, repair preparation and displaying current activity on resume require the same Windows user and machine. Connection settings are not stored as plaintext in the state file. Snapshot and report files themselves are not encrypted.

Scratch files are removed when their phase exits normally or through a handled error. In persistent mode, a terminated process can leave scratch or `.partial` files behind; the next attempt at that phase overwrites them. Completed persistent snapshots are retained for verification and resuming.

## Snapshot format

Both data snapshots are UTF-8 tab-separated files (`.tsv`) without a BOM, using LF line endings. The first line is a column header; every subsequent line contains one record. Records remain ordered by `VersionId`, even though `nodeId` is the first column. Writing and reading are streamed one record at a time.

Database column order and an example row (separators below are actual TAB characters):

```text
nodeId	versionId	nodeTimestamp	versionTimestamp	lastMajorVersionId	lastMinorVersionId
1	10	100	200		10
```

Index column order and an example row:

```text
nodeId	versionId	nodeTimestamp	versionTimestamp	isLastPublic	isLastDraft	docId	presentFields	draftDocId	historicalPathMatches	missingOwnPath	missingDraftPath
1	10	100	200	0	1	0	63	0	0	0	0
```

Numbers use culture-independent decimal notation, retaining full 64-bit integer precision for timestamps. Boolean fields use `0` for false and `1` for true. SQL null references in `lastMajorVersionId` and `lastMinorVersionId` are empty fields; a numeric zero remains `0`. Trailing empty fields are preserved. Data fields contain no free text and require no quoting or escaping.

`presentFields` is a decimal bitmask identifying the fields found in the index:

| Bit value | Field |
| --- | --- |
| 1 | `NodeId` |
| 2 | `NodeTimestamp` |
| 4 | `VersionTimestamp` |
| 8 | `IsLastPublic` |
| 16 | `IsLastDraft` |
| 32 | `VersionId` |

The value `63` means all six fields are present. The draft reference and path flags carry the information needed for historical timestamp checks; they do not contain full paths.

The data row count (excluding the header), byte length, and SHA-256 checksum are tracked during execution and stored in `workflow.json` in persistent mode. Readers validate the header and column order, reject incorrect column counts or values, and verify the expected number of data rows.

Binary and JSONL checkpoints in a workspace are automatically converted to TSV when resuming with `-w`, without querying either source. Conversion validates the saved checksums, streams the records into new files, and commits the updated state before deleting the converted inputs. If conversion fails before that state update, the input checkpoint remains available for retry. Allow space for both inputs and TSV outputs during conversion. A completed comparison report is preserved.

## Scope of the check

- A database version missing from the index: `NotInIndex`.
- An index document absent from the checked SQL versions: `NotInDatabase`.
- Multiple documents with the same `VersionId`: each additional document is reported as `MoreDocument`; duplicate documents are also checked.
- Matching `NodeId`, `VersionTimestamp`, and `NodeTimestamp` values.
- `IsLastPublic == (VersionId == LastMajorVersionId)` and `IsLastDraft == (VersionId == LastMinorVersionId)`.
- Missing or invalid required fields, and required fields indexed with multiple distinct values.

A version whose indexed `IsLastDraft` value is not `yes` may have a `NodeTimestamp` different from SQL if its stored path matches that of the first current draft found in the index for the same node. Index export loads only the stored `Path` field for these comparisons and saves equality and missing-path information. The comparison phase applies this exception only when timestamps differ. Missing paths cannot hide a timestamp difference.

Deleted Lucene documents and `$#COMMIT` documents are excluded. Documents without a `VersionId` are checked separately. Required Boolean values use sensenet's `yes` and `no` terms.

The tool checks the **searchable, indexed values** of numeric fields and flags. It does not compare every business field, separately verify stored and indexed representations, or compare full SQL-side paths. Matching timestamps are accepted as evidence that business fields are correct. It does not start a sensenet repository; repair rewrites only the affected versions and documents needed to restore them.

## Large datasets and performance

Database export streams `NodeId`, `VersionId`, `NodeTimestamp`, `VersionTimestamp`, `LastMajorVersionId`, and `LastMinorVersionId` from one ordered version query into a TSV file.

Index export reads `Id`, `NodeTimestamp`, `VersionTimestamp`, `IsLastPublic`, and `IsLastDraft` directly through terms and postings, using only full-precision numeric terms. Metadata is kept in a disk-backed memory-mapped file addressed by DocId. Traversing `Id` postings identifies each node's current draft and prepares historical path metadata. Full-precision `VersionId` terms already provide numeric order, so no additional sort is needed.

Comparison sequentially merges the saved streams, retaining only current records and writing differences immediately. During comparison, the tool does not keep full SQL results, index results, or difference lists in managed memory. Document metadata and missing-path tracking use disk storage. Repair keeps the report differences, affected DocId/VersionId sets and per-version fingerprints in memory; SQL document payloads are loaded one version at a time. Lucene's internal structures and the operating system's caches still consume memory; resident memory is not fixed. Use a 64-bit process for large indexes.

Disk space requirements:

- Database snapshot: one header line plus six tab-separated fields per SQL version; its byte size depends on the numeric values and empty null references.
- Index snapshot: one header line plus twelve tab-separated fields per exported VersionId/DocId pair; its byte size depends on the numeric values. Boolean values occupy one character each.
- Index export scratch: 32 bytes per Lucene `MaxDoc` slot, with a one-byte minimum. Deleted document slots also contribute to `MaxDoc`.
- Comparison scratch: two bitsets of `ceil(MaxDoc / 8)` bytes each, for missing draft paths and distinct affected index documents.
- Index issues and comparison reports: variable size, proportional to reported differences.

During index export, allow space for both snapshots, metadata scratch, and issues. Historical versions add stored-`Path` reads during export. SQL ordering, Lucene traversal, disk throughput, path reads, and the number of differences determine runtime. A 100,000-record streaming comparison is covered by tests; no production-scale speedup has been measured.

## Output and exit codes

The comparison report contains `type: "difference"` records followed by one `type: "summary"` record with counts, comparison elapsed time and `lastActivity`. This activity object contains `database` and `index`; the latter contains `lastActivityId`, `gapPreview` (up to five IDs) and `gapCount` (the full count). These values come from the actual SQL identity and Lucene commit metadata captured alongside the compared snapshots. The same activity summary is saved in the workflow comparison checkpoint. After a successful repair commit, one `type: "repair_completed"` JSON record is appended to the active comparison file with `workflowId`, `deletedDocuments`, `writtenVersions`, `lastActivityId` and empty `gaps`. Existing report bytes are preserved. Its length, row count and SHA-256 checksum are updated before saving `RepairCompleted`; reopening a completed workflow does not append another record. With `-w` the report is retained. In temporary mode the same record is written before the working directory is cleaned up.

Standard output shows an aligned text block headed `COMPARISON SUMMARY:` with status and the complete comparison counts. `COUNTS BY KIND` appears only when there are differences and lists nonzero counts. `LAST ACTIVITY` and the closing separator always appear. This section uses the saved comparison activity: the database value is the last allocated SQL indexing activity identity at export; the index value comes from the exported Lucene commit. A nonempty gap list shows at most five IDs; longer lists end with `... N total`, where N is the total number of gaps.

The `COMPARISON SUMMARY` activity remains unchanged when the workflow is reopened or repaired; displaying it does not read the sources again. A report missing `lastActivity` is enriched from its saved snapshot metadata, preserving any repair completion record and updating the report checksum. If no complete activity state was saved, a new full check is required; current values cannot reconstruct the historical state. The `REPAIR SUMMARY` reads SQL and the current Lucene commit again. This query is SELECT-only; reading Lucene metadata does not write to the index or load the repository schema. If either source cannot be read for the repair summary, the tool reports an error and returns exit code 2 without printing a placeholder repair summary.

Without `-w`, up to N JSON difference records precede the text summary (default 20, set with `--max-console-differences`). Redirection keeps the same readable summary, so stdout contains mixed JSON records and text in temporary mode. The limit does not stop checking or limit the summary counts. A failed or cancelled comparison emits no summary. With `-w`, the complete machine-readable comparison remains in the saved JSONL report. Mode, progress, the report path, the output-limit notice, checkpoints, cleanup messages and runtime errors go to standard error.
```text
COMPARISON SUMMARY:
  status:                      differences
  databaseVersions:             182
  indexDocuments:               177
  matchedDocuments:             177
  differences:                    9
  differentDocuments:             7
  COUNTS BY KIND:
    DifferentVersionTimestamp:    2
    DifferentNodeTimestamp:       2
    NotInIndex:                   5
  LAST ACTIVITY:
    database:                  1950
    index:                     1897 (1800, 1801, 1802, 1803, 1804, ... 8 total)
-----------------------------------------------
```

The saved JSON comparison summary and the console `COMPARISON SUMMARY` include `differences`, the number of reported difference records, and `differentDocuments`, the number of distinct affected documents:

- Each indexed document referenced by a difference is counted once by `DocId`, even if it has several field errors, appears under multiple version IDs, or is reported during both index export and comparison.
- Each SQL version missing from the index (`NotInIndex`) adds one expected document, counted by `VersionId`.
- Versions of the same node and separate duplicate index documents are separate documents. A document counts only when a difference refers to it.

For example, two documents with both timestamp fields differing produce `"differences": 4` and `"differentDocuments": 2`. If an indexed document has no usable version ID, it cannot be paired with a missing SQL version; the two unmatched entries are counted separately.

In persistent mode, the count is saved in `workflow.json`. Index document uniqueness uses a disk-backed bitset in both modes, while missing SQL versions are processed in order without a growing in-memory set.

When a saved summary lacks `differentDocuments`, resuming computes it by streaming the saved difference records. It preserves those records and their existing summary data, without querying sources or repeating comparison. A complete replacement report is written before the state switches to it; the input report is then removed. Subsequent resumes reuse the updated summary.

```powershell
.\publish\IndexIntegrityChecker.exe -w "D:\Checks\check-001" > summary.txt
$LASTEXITCODE
```

| Exit code | Meaning |
| --- | --- |
| 0 | No repair is needed, or the confirmed repair completed successfully. |
| 1 | Differences or an activity-state correction remain; repair was not confirmed. |
| 2 | Invalid arguments, damaged files, changed sources, or failed checking/preparation/repair. |
| 130 | Checking or repair was cancelled with Ctrl+C. |

## Content types with indexing disabled

The tool reads the `NodeTypes` hierarchy and the `Binary` XML definitions from the latest drafts of content types under `/Root/System/Schema/ContentTypes`. It evaluates `AllowIndexing`, including inheritance, and excludes disabled types from the SQL version list. Remaining index documents for excluded types are reported as differences.

Definitions must be readable from `Files.Stream`. An external blob provider or missing definition stream causes an explicit runtime error because no external blob-store access settings are supplied. The tool does not use `Versions.IndexDocument IS NOT NULL` to exclude versions, since that could hide missing indexing data.

## Running a consistent check

Use matching database and index snapshots, or pause writes and indexing while creating the exports. Lucene and SQL do not share a transaction. Resuming cannot make independently captured sources consistent.

SQL data is read within a single transaction during database export, using snapshot isolation when enabled and `Serializable` otherwise. Serializable can hold shared and range locks until database export completes and may block writers. The transaction closes before index export and comparison. The tool does not change database settings or use `NOLOCK`. Queries can be cancelled with Ctrl+C.

The index commit captured at initialization must remain available and unchanged until repair starts. The tool verifies it during export, preparation and immediately before writing. A changed commit requires a new full check. Checking uses read-only readers; repair uses normal Lucene write locks and never forces an unlock or deletes another writer's lock.

## Offline IndexOnly repair

After comparison, the tool prepares repair from the complete `comparison-<id>.jsonl` report (or the active summary-enriched report). It reads existing `Versions.IndexDocument` values and current metadata with SELECT statements. SQL documents, content, activity rows and processing flags are never changed. Missing or invalid SQL index documents and unsupported field settings stop preparation before the first index write.

The operator must back up/copy the index and keep SQL and the index unchanged by other processes from checking through repair, including any pause between runs. The tool does not copy indexes. Keep these sources available while confirming repair: the read-only SQL transaction remains open during that prompt.

Preparation displays `Preparing index repair...`. The prompt shows document deletions, version writes and the final activity id, then asks `Perform the repair? [yes/NO]`. Interactive answers `yes` and `y` start repair, ignoring letter case and surrounding whitespace. Every other answer, EOF, empty input or redirected/non-interactive stdin declines repair. With `-w`, `RepairPrepared` remains resumable using the same command and workspace; preparation is repeated and confirmation is requested again. Temporary runs are cleaned up after the prompt/repair and cannot be resumed.

Repair removes affected documents by their original DocIds before opening the writer, then writes the required individual versions. A malformed document containing several version ids is expanded to include every affected version and duplicate. Excluded/deleted versions are removed without reinsertion. The successful final commit stores the SQL identity's last activity id and an empty gap list (the old `Gaps` key is removed), preserving other user-data. Metadata-only repairs force a commit by replacing the `$#COMMIT` marker with a new `$#DATA` GUID.

The repair is not atomic and cannot resume after `RepairStarted`. If interrupted, run a new full check in a new workspace, even if interruption occurred before the first actual index write or after the final index commit. Intermediate commits preserve the prior activity state. The successful final commit is followed by appending `repair_completed` to the active comparison report and saving `RepairCompleted`. A failure while recording that completion leaves the workflow at `RepairStarted`, requiring a new full check. The console displays a text `REPAIR SUMMARY` block in the comparison-summary style, with status, deleted documents, written versions, fresh `LAST ACTIVITY` and a closing separator. The JSON completion record is written only to the report. Comparison differences and counts still describe the pre-repair result. If only displaying the completed result fails, the completion record and checkpoint remain valid and the message identifies the display failure. Verify consistency with a separate fresh check.

Field modes, storage, term vectors, numeric encodings and analyzers follow local adaptations of the MSSQL/Lucene indexing code. CTDs, SQL-stored aspect/list definitions, supported standard handler defaults and explicit Keyword/Standard/Whitespace analyzers are resolved offline. `Sharing` and its built-in indexing handlers default to Keyword; repair replays the sharing token array stored in the SQL index document, including compound terms, without loading repository sharing objects. `_Text` uses Standard. Unresolved dynamic fields, custom handlers/configuration or conflicting settings fail before writing. Experimental API elements carry `TODO(refactor)` comments; copied elements identify their original source and commit. The sensenet repositories are not modified.

## Building and testing

### Source map

| Responsibility | Main files |
| --- | --- |
| Arguments, confirmation, console output and exit codes | [CommandLineOptions.cs](src/IndexIntegrityChecker/CommandLineOptions.cs), [Program.cs](src/IndexIntegrityChecker/Program.cs) |
| Phase transitions, workspace ownership and persisted state | [WorkflowRunner.cs](src/IndexIntegrityChecker/WorkflowRunner.cs), [WorkflowWorkspace.cs](src/IndexIntegrityChecker/WorkflowWorkspace.cs), [WorkflowState.cs](src/IndexIntegrityChecker/WorkflowState.cs) |
| SQL reads, indexing exclusions and snapshot export | [SqlVersionSource.cs](src/IndexIntegrityChecker/SqlVersionSource.cs), [ContentTypePolicy.cs](src/IndexIntegrityChecker/ContentTypePolicy.cs), [WorkflowOperations.cs](src/IndexIntegrityChecker/WorkflowOperations.cs) |
| Lucene extraction and comparison | [LuceneSnapshot.cs](src/IndexIntegrityChecker/LuceneSnapshot.cs), [IntegrityChecker.cs](src/IndexIntegrityChecker/IntegrityChecker.cs), [SavedComparison.cs](src/IndexIntegrityChecker/SavedComparison.cs) |
| Snapshot/report persistence and compatibility | [CheckpointFiles.cs](src/IndexIntegrityChecker/CheckpointFiles.cs), [SnapshotFiles.cs](src/IndexIntegrityChecker/SnapshotFiles.cs), [WorkflowSnapshotUpgrade.cs](src/IndexIntegrityChecker/WorkflowSnapshotUpgrade.cs), [ComparisonSummaryUpgrade.cs](src/IndexIntegrityChecker/ComparisonSummaryUpgrade.cs) |
| Repair planning and source validation | [ComparisonRepairer.cs](src/IndexIntegrityChecker/ComparisonRepairer.cs), [MsSqlRepairSource.cs](src/IndexIntegrityChecker/MsSqlRepairSource.cs) |
| SQL index-document conversion, schema/analyzers and Lucene writes | [RepairDocuments.cs](src/IndexIntegrityChecker/RepairDocuments.cs), [LuceneRepairWriter.cs](src/IndexIntegrityChecker/LuceneRepairWriter.cs) |

The project targets MSSQL and Lucene 2.9 directly. It has no runtime repository dependency or general provider layer. `IWorkflowOperations`, the repair factory and confirmation callbacks, and the repairer's row/activity delegates are test seams. `InternalsVisibleTo("IndexIntegrityChecker.Tests")` exposes internal contracts to the test assembly.

### Invariants when changing code

- Production SQL remains read-only. Reuse existing SQL `IndexDocument` data; do not generate it by loading repository objects or change the original sensenet repositories. Every experimental API element needs a `TODO(refactor)` comment; copied/adapted code also names the original source and commit.
- Keep comparison behavior consistent between `IntegrityChecker` and `SavedComparison`, including historical paths, excluded types and malformed/duplicate documents. Changing a difference kind also affects report parsing and repair planning.
- A DocId belongs to the captured Lucene commit. Resolve affected documents and versions before mutation; do not reuse original DocIds after writer merges may have renumbered documents. Respect Lucene's write lock.
- Persist `RepairStarted` before the first index mutation. Preserve the order: successful final index commit, append `repair_completed` and update report metadata, then save `RepairCompleted`. Failures after the start marker require a new full check; add no per-document resume path.
- Keep comparison activity tied to its source snapshots in both the report and checkpoint. Console comparison output uses that saved state; only the repair summary reads live activity. Gap previews remain bounded to five IDs plus the total count. Final repair commits clear gaps and store the last allocated SQL activity identity, including metadata-only repairs.
- Persist reports/checkpoints atomically where supported, retain length/row-count/SHA-256 verification and preserve completed repair records during summary upgrades. Extend saved contracts compatibly or provide an explicit validated migration. Never turn missing source metadata into a fabricated successful result.

### Build and test commands

Building requires the .NET 8 SDK or newer; running requires the .NET 8 runtime. Creating a persistent workflow requires Windows because saved connection settings use Windows DPAPI. Temporary mode does not use DPAPI. Dependencies are NuGet packages, with no references to local sensenet source paths.

```powershell
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-restore
dotnet publish .\src\IndexIntegrityChecker\IndexIntegrityChecker.csproj -c Release --no-restore -o .\publish
```

Tests cover temporary output limits and complete summaries, CLI validation, cleanup, persistent checkpoint recovery, corruption, Windows connection protection, comparison parity, snapshot formats and a 100,000-record streaming comparison. Real temporary Lucene indexes cover all 11 repair difference kinds, multi-version closure, old-version isolation, schema/analyzer conversion, metadata-only commits, prepared continuation, start-marker failures and interruption during repair. Repair tests use in-memory SQL rows; live MSSQL repair-source behavior requires separate environment validation.

Optional SQL integration tests cover checker queries, repair row conversion and the last identity value after activity cleanup. They create only connection-local `#` temporary tables; they do not create persistent tables or databases, or modify existing data:

```powershell
$env:INDEX_CHECKER_TEST_SQL_CONNECTION = 'Server=localhost;Database=tempdb;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
dotnet test -c Release --filter TestCategory=SqlIntegration
```

For a repair change, the focused suite can be run before the full test suite:

```powershell
dotnet test -c Release --no-restore --filter "FullyQualifiedName~RepairTests|FullyQualifiedName~RepairWorkflowTests"
```

Changes to persisted formats or summaries also need the workflow, snapshot-format, different-document and temporary-mode tests. Without `INDEX_CHECKER_TEST_SQL_CONNECTION`, SQL integration tests are skipped; ordinary tests are not evidence that the live SQL source works in a particular environment. Keep destructive fixtures confined to temporary test indexes and connection-local SQL temporary tables.

Maintain [readme.md](readme.md) and [readme-hu.md](readme-hu.md) as short user guides, and this file and [readme-hu-for-dev.md](readme-hu-for-dev.md) as detailed developer references. Update both languages for each affected audience, including CLI examples and limitations.
