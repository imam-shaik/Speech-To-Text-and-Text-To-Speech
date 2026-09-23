# Speech-to-Text Certification Suite

## Overview

This directory contains the permanent regression baseline for the SubtitleEdit Speech-to-Text module. Once established, this baseline enables objective, repeatable certification of any future changes.

## Directory Structure

```
SpeechToTextCertification/
├── TestMedia/              # Canonical test videos (SHA256-verified)
├── Expected/               # Golden output files
│   ├── Whisper/
│   │   ├── Legacy/
│   │   └── Chunked/
│   └── Vosk/
│       ├── Legacy/
│       └── Chunked/
├── Reports/                # Certification run reports (JSON + text)
├── Metrics/                # Longitudinal performance data
└── README.md               # This file
```

## Test Media (STT-001 through STT-005)

| ID      | Content               | Duration | Language | Purpose                              |
|---------|-----------------------|----------|----------|--------------------------------------|
| STT-001 | English lecture       | 30s      | English  | Basic transcription                  |
| STT-002 | English interview     | 2 min    | English  | Conversation                         |
| STT-003 | English podcast       | 5 min    | English  | Long continuous speech               |
| STT-004 | Hindi speech          | 2 min    | Hindi    | Vosk/translation validation          |
| STT-005 | Mixed Hindi + English | 2–5 min  | Mixed    | Code-switching & bilingual testing   |

Each test media file must have its SHA256 hash recorded before use. If the hash changes, the file must not be used for regression testing.

## Expected Outputs

For each test media × engine × mode combination, the following should be captured and stored:

- **SRT file** — Final subtitle output
- **Translation SRT** — If translation is enabled
- **Bilingual SRT** — If bilingual output is enabled

## Certification Run Output

Each certification run produces:

1. **Human-readable report** (`*.txt`) — For manual review
2. **Structured JSON** (`*.json`) — For automated comparison
3. **Metrics snapshot** — For longitudinal tracking

## Running Certification

```csharp
// Example using existing CertificationSuite
var testCases = new[]
{
    new VideoTestCase("path/to/STT-001.mp4", VideoType.SingleSpeaker),
    new VideoTestCase("path/to/STT-002.mp4", VideoType.Conversation),
    // ...
};

var suite = new CertificationSuite(testCases, engine, modelName, language);
var report = await suite.RunAsync();

report.SaveToFile("Reports/report.txt");
report.SaveJsonToFile("Reports/report.json");
```

## Baseline Establishment

Before runtime certification, establish the baseline by:

1. Acquiring representative test media
2. Computing SHA256 hashes for each file
3. Running all engine × mode combinations
4. Storing the golden outputs in `Expected/`
5. Recording run metrics in `Metrics/`

## Regression Criteria

A certification run passes if:

- All timestamps are valid
- Subtitle count is within ±10% of baseline
- Runtime is within ±50% of baseline
- Memory delta is <100MB
- All temp files cleaned up
- No orphan processes

## Adding New Test Media

1. Add the file to `TestMedia/`
2. Compute SHA256: `Get-FileHash -Algorithm SHA256 path\to\file.mp4`
3. Add entry to the test media table above
4. Run baseline establishment
5. Commit with descriptive message

## Maintenance

This baseline should be reviewed annually or when:

- Major engine updates are released
- Significant UI changes are made
- New engines are added
- Translation providers change

---

Last Updated: 2026-06-27
Baseline Commit: <to be recorded after baseline establishment>