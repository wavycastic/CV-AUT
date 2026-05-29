import subprocess
import os
import sys
import numpy as np
import cv2

class ADBHelper:
    """
    Lightweight, optimized wrapper for Android Debug Bridge (ADB) operations.
    Focuses on minimal CPU and RAM usage.
    """
    def __init__(self, host="127.0.0.1", port=5556):
        self.device_address = f"{host}:{port}"
        # Locate standard adb in PATH, or default to a local string
        self.adb_bin = "adb"
        self.create_no_window = subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0
        self._ensure_connection()

    def _ensure_connection(self):
        """Attempts to connect to the target virtual device."""
        try:
            # Check if connected
            result = subprocess.run(
                [self.adb_bin, "connect", self.device_address],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                creationflags=self.create_no_window,
                text=True
            )
            print(f"[ADB] Connection result: {result.stdout.strip()}")
        except FileNotFoundError:
            print("[ADB WARNING] 'adb' command not found in system PATH. Please ensure ADB is installed.")

    def execute_shell(self, command_list: list) -> str:
        """Executes a generic shell command on the target device."""
        try:
            res = subprocess.run(
                [self.adb_bin, "-s", self.device_address, "shell"] + command_list,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                creationflags=self.create_no_window,
                text=True
            )
            return res.stdout.strip()
        except Exception as e:
            return f"Error: {e}"

    def tap(self, x: int, y: int):
        """Simulates a tap at coordinates (x, y)."""
        subprocess.run(
            [self.adb_bin, "-s", self.device_address, "shell", "input", "tap", str(x), str(y)],
            creationflags=self.create_no_window
        )

    def swipe(self, x1: int, y1: int, x2: int, y2: int, duration_ms: int = 300):
        """Simulates a swipe gesture from (x1, y1) to (x2, y2)."""
        subprocess.run(
            [self.adb_bin, "-s", self.device_address, "shell", "input", "swipe", 
             str(x1), str(y1), str(x2), str(y2), str(duration_ms)],
            creationflags=self.create_no_window
        )

    def take_screenshot(self) -> np.ndarray:
        """
        Captures the screen and returns a NumPy/OpenCV BGR image.
        Uses optimized memory buffers instead of writing to disk.
        """
        try:
            # Run screencap via stdout pipe
            pipe = subprocess.Popen(
                [self.adb_bin, "-s", self.device_address, "exec-out", "screencap", "-p"],
                stdout=subprocess.PIPE,
                creationflags=self.create_no_window
            )
            image_bytes = pipe.stdout.read()
            
            # Convert direct stdout bytes into OpenCV image matrix (fast, no disk IO)
            image_array = np.frombuffer(image_bytes, dtype=np.uint8)
            img = cv2.imdecode(image_array, cv2.IMREAD_COLOR)
            return img
        except Exception as e:
            print(f"[ADB ERROR] Screenshot capture failed: {e}")
            return None
