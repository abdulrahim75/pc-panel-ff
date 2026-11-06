from pathlib import Path
from typing import Iterable, NamedTuple

import cv2
import numpy as np
from ultralytics import YOLO


class Detection(NamedTuple):
    box: tuple[int, int, int, int]
    confidence: float

    @property
    def estimated_head_box(self) -> tuple[int, int, int, int]:
        x1, y1, x2, y2 = self.box
        person_height = y2 - y1
        head_height = max(1, round(person_height * 0.20))
        head_width = max(1, round((x2 - x1) * 0.55))
        head_center_x = (x1 + x2) // 2
        return (
            max(x1, head_center_x - head_width // 2),
            y1,
            min(x2, head_center_x + head_width // 2),
            min(y2, y1 + head_height),
        )


class PersonDetector:
    PERSON_CLASS_ID = 0

    def __init__(
        self,
        model_path: str | Path = "models/yolo11n.pt",
        confidence_threshold: float = 0.25,
    ) -> None:
        self.model_path = Path(model_path).expanduser()
        if not self.model_path.is_file():
            raise FileNotFoundError(
                f"YOLO model was not found: {self.model_path}. "
                "Download a local Ultralytics YOLO model and pass it with --model."
            )
        if not 0.0 <= confidence_threshold <= 1.0:
            raise ValueError("Confidence threshold must be between 0 and 1.")

        self.confidence_threshold = confidence_threshold
        self.model = YOLO(str(self.model_path))

    def detect(self, frame: np.ndarray) -> list[Detection]:
        results = self.model.predict(
            frame,
            classes=[self.PERSON_CLASS_ID],
            conf=self.confidence_threshold,
            verbose=False,
        )
        detections: list[Detection] = []

        for result in results:
            if result.boxes is None:
                continue
            for box, confidence, class_id in zip(
                result.boxes.xyxy.cpu().numpy(),
                result.boxes.conf.cpu().numpy(),
                result.boxes.cls.cpu().numpy(),
            ):
                if int(class_id) != self.PERSON_CLASS_ID:
                    continue
                x1, y1, x2, y2 = map(int, box)
                detections.append(Detection((x1, y1, x2, y2), float(confidence)))
        return detections

    def annotate(self, frame: np.ndarray, detections: Iterable[Detection]) -> np.ndarray:
        annotated = frame.copy()
        for detection in detections:
            x1, y1, x2, y2 = detection.box
            cv2.rectangle(annotated, (x1, y1), (x2, y2), (0, 255, 0), 2)
            cv2.putText(
                annotated,
                f"Person {detection.confidence:.2f}",
                (x1, max(20, y1 - 8)),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                (0, 255, 0),
                2,
                cv2.LINE_AA,
            )

            hx1, hy1, hx2, hy2 = detection.estimated_head_box
            cv2.rectangle(annotated, (hx1, hy1), (hx2, hy2), (0, 180, 255), 2)
            cv2.putText(
                annotated,
                "Estimated head region",
                (hx1, max(20, hy1 - 8)),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.45,
                (0, 180, 255),
                1,
                cv2.LINE_AA,
            )
        return annotated
