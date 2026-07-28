# Swarm Project Matrix: NewDicomMerger

> **Commander's Note:** This file is the Single Source of Truth (SSOT). Always run `python swarm_cli.py sync` after making any edits to propagate changes to the database.

## 1. Project Goal & Architecture
**Objective:** Analyze the NewDicomMerger codebase and propose numbered extension/optimization features using a Multi-Agent Swarm approach.
**MECE Guarantee:** Tasks are divided by domain: DICOM/NIfTI standards and backend logic (Deep_Research_Analyst), performance & security (QA_Security_Auditor), and UI/UX & preview capabilities (Frontend_Architect_Elite).

## 2. Agent Roster (Live Tracking)
*Update this table, then run `swarm_cli.py sync` to propagate changes to the JSON state.*

| Agent UUID | Persona | Status | Workspace | Allowed Context (Paths) | Memory Refresh Strategy |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `agent-research-01` | `Deep_Research_Analyst` | COMPLETED | `inherit` | `[/Services/]` | None required |
| `agent-qa-01` | `QA_Security_Auditor` | COMPLETED | `inherit` | `[/Services/, /MainWindow.xaml.cs]` | None required |
| `agent-ui-01` | `Frontend_Architect_Elite` | COMPLETED | `inherit` | `[/MainWindow.xaml, /MainWindow.xaml.cs]` | None required |
| `agent-backend-01` | `Backend_Logic_Engineer` | COMPLETED | `inherit` | `[/Services/, /MainWindow.xaml.cs]` | None required |

## 3. MECE Execution Graph
*Strict dependency tracking and complexity scoring.*

| Task ID | Description (MECE) | Assigned Persona | Depends On | Complexity (1-10) | Target Paths | Fallback / Escalation | Status |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| TSK-01 | Analyze DICOM and NIfTI operations (Merger, Splitter, Converter) for standard conformity and robustness. | Deep_Research_Analyst | None | 6 | `/Services/` | None | COMPLETED |
| TSK-02 | Review performance (caching, parallelization) and error-handling in file processing. | QA_Security_Auditor | None | 5 | `/Services/`, `/MainWindow.xaml.cs` | None | COMPLETED |
| TSK-03 | Analyze UI/UX flow, preview responsiveness, and window/level layout. | Frontend_Architect_Elite | None | 5 | `/MainWindow.xaml` | None | COMPLETED |
| TSK-04 | Implement Option 2: SharedFunctionalGroupsSequence fallback logic in FrameSplitter. | Backend_Logic_Engineer | TSK-01 | 4 | `/Services/FrameSplitter.cs` | None | COMPLETED |
| TSK-05 | Implement Option 1: Live Preview WriteableBitmap Caching & Prefetching. | Backend_Logic_Engineer | TSK-02, TSK-03 | 5 | `/MainWindow.xaml.cs` | None | COMPLETED |
| TSK-06 | Implement Option 3 (DicomTranscoder decompression) and Option 4 (Window/Level Presets & Hotkeys). | Backend_Logic_Engineer | None | 5 | `/Services/`, `/MainWindow.xaml.cs` | None | COMPLETED |
| TSK-07 | Implement Option 5 (MPR 3D-Volumen loading and Direct Pointer Gray8 rendering). | Frontend_Architect_Elite | None | 7 | `/MainWindow.xaml.cs` | None | COMPLETED |
| TSK-08 | Implement Option 6 (Radiology Mode Theme-Toggle). | Frontend_Architect_Elite | None | 4 | `/MainWindow.xaml` | None | COMPLETED |
| TSK-09 | Implement Output Compression (JPEG Lossless, ZIP-Archiving, Direct NIfTI-Export). | Backend_Logic_Engineer | None | 6 | `/Services/`, `/MainWindow.xaml.cs` | None | COMPLETED |

## 4. Final Quality Audit Protocol
| Auditor Persona | Verification Criteria | Status |
| :--- | :--- | :--- |
| `QA_Security_Auditor` | Verify that proposed optimizations cover all major system components | PASSED |
| `Executive_Editor_QA` | Ensure proposed improvements are highly structured and actionable | PASSED |
| `QA_Security_Auditor` | Verify build passes and preview performance is optimal | PASSED |
| `QA_Security_Auditor` | Verify all 6 options and compression features are fully implemented | PASSED |
