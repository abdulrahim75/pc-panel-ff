import struct
from pathlib import Path

import cv2
import numpy as np

from main import send_jpeg_frame


class CaptureSocket:
    def __init__(self) -> None:
        self.data = bytearray()

    def sendall(self, data: bytes) -> None:
        self.data.extend(data)


def main() -> int:
    frame = np.full((120, 160, 3), 127, dtype=np.uint8)
    socket = CaptureSocket()
    send_jpeg_frame(socket, frame)

    if len(socket.data) < 4:
        raise AssertionError("Frame payload header was not written.")
    length = struct.unpack("<I", socket.data[:4])[0]
    jpeg = bytes(socket.data[4:])
    if length != len(jpeg):
        raise AssertionError("Frame payload length does not match JPEG data.")
    decoded = cv2.imdecode(np.frombuffer(jpeg, dtype=np.uint8), cv2.IMREAD_COLOR)
    if decoded is None or decoded.shape != frame.shape:
        raise AssertionError("JPEG frame could not be decoded by OpenCV.")
    print(f"Frame protocol test passed: {length} JPEG bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
