# HeadshotPanel Python Engine

This independent Python engine performs offline person detection on prerecorded video files. It does not connect to or interact with any running application or game, and it does not include aiming, shooting, game-memory, or anti-cheat functionality.

## Prerequisites

- Python 3.10 or newer
- A local Ultralytics YOLO `.pt` model

## Setup on Windows

From the repository root:

```powershell
cd python-engine
.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

Place a YOLO model in:

```text
python-engine/models/yolo11n.pt
```

The application intentionally does not download a model automatically. Obtain a model separately and keep it local.

## Run

```powershell
python main.py C:\path\to\recorded-video.mp4
```

To use another local model or confidence threshold:

```powershell
python main.py C:\path\to\recorded-video.mp4 --model C:\path\to\model.pt --confidence 0.30
```

Press `Q` or `Esc` to close the video window.

## Display labels

Green boxes show YOLO person detections and their confidence scores. Orange boxes show estimated head regions derived approximately from the top 20% of each person box. They are estimates, not trained head detections.
