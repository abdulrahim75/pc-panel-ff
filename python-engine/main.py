import argparse
import socket
import struct
import sys
from pathlib import Path

import cv2

from detection.detector import PersonDetector


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Run offline person detection on a prerecorded video."
    )
    parser.add_argument("video", type=Path, help="Path to a prerecorded video file.")
    parser.add_argument(
        "--model",
        type=Path,
        default=Path(__file__).resolve().parent / "models" / "yolo11n.pt",
        help="Path to a local YOLO .pt model.",
    )
    parser.add_argument(
        "--confidence",
        type=float,
        default=0.25,
        help="Person detection confidence threshold from 0 to 1 (default: 0.25).",
    )
    parser.add_argument(
        "--window",
        default="HeadshotPanel - Prerecorded Video Analysis",
        help="OpenCV window title.",
    )
    parser.add_argument(
        "--frame-port",
        type=int,
        help="Send JPEG frames to a local TCP IPC port instead of opening an OpenCV window.",
    )
    return parser


def connect_frame_receiver(port: int) -> socket.socket:
    receiver = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    receiver.settimeout(10)
    receiver.connect(("127.0.0.1", port))
    receiver.settimeout(None)
    return receiver


def send_jpeg_frame(receiver: socket.socket | None, frame) -> None:
    if receiver is None:
        return
    ok, encoded = cv2.imencode(".jpg", frame, [cv2.IMWRITE_JPEG_QUALITY, 82])
    if not ok:
        raise RuntimeError("OpenCV could not JPEG-encode an annotated frame.")
    payload = encoded.tobytes()
    receiver.sendall(struct.pack("<I", len(payload)) + payload)


def run(video_path: Path, model_path: Path, confidence: float, window: str, frame_port: int | None) -> int:
    video_path = video_path.expanduser().resolve()
    model_path = model_path.expanduser().resolve()

    if not video_path.is_file():
        print(f"Error: video file was not found: {video_path}", file=sys.stderr)
        return 2

    try:
        detector = PersonDetector(model_path, confidence)
    except (FileNotFoundError, ValueError, OSError) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 3
    except Exception as error:
        print(f"Error: unable to load YOLO model: {error}", file=sys.stderr)
        return 3

    capture = cv2.VideoCapture(str(video_path))
    if not capture.isOpened():
        capture.release()
        print(f"Error: OpenCV could not open the video file: {video_path}", file=sys.stderr)
        return 4

    frame_receiver = None
    if frame_port is not None:
        try:
            frame_receiver = connect_frame_receiver(frame_port)
        except (OSError, TimeoutError) as error:
            capture.release()
            print(f"Error: could not connect to frame receiver: {error}", file=sys.stderr)
            return 6
    else:
        cv2.namedWindow(window, cv2.WINDOW_NORMAL)
    try:
        while True:
            ok, frame = capture.read()
            if not ok:
                break

            detections = detector.detect(frame)
            processed = detector.annotate(frame, detections)
            if frame_receiver is not None:
                send_jpeg_frame(frame_receiver, processed)
            else:
                cv2.imshow(window, processed)
                key = cv2.waitKey(1) & 0xFF
                if key in (27, ord("q")):
                    break
    except (cv2.error, OSError, RuntimeError, ConnectionError) as error:
        print(f"Error: OpenCV processing failed: {error}", file=sys.stderr)
        return 5
    finally:
        capture.release()
        if frame_receiver is not None:
            frame_receiver.close()
        else:
            cv2.destroyWindow(window)

    return 0


def main() -> int:
    args = build_parser().parse_args()
    return run(args.video, args.model, args.confidence, args.window, args.frame_port)


if __name__ == "__main__":
    raise SystemExit(main())
