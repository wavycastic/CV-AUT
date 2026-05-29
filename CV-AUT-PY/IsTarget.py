import os
import cv2
import sys
from adb_helper import ADBHelper
from vision_engine import VisionEngine

# Tọa độ vùng quét tài nguyên ở độ phân giải 1600x900px
COORDS = {
    "Gold": (55, 117, 251, 161),
    "Elixir": (60, 167, 261, 208),
    "Dark Elixir": (73, 214, 183, 248)
}

# Lề (margin) cắt bù trừ cho từng loại tài nguyên
PADDING = {
    "Gold": {"l": 60, "r": 15, "t": 5, "b": 5},
    "Elixir": {"l": 15, "r": 15, "t": 5, "b": 5},
    "Dark Elixir": {"l": 15, "r": 15, "t": 5, "b": 5}
}

def extract_resources(adb: ADBHelper = None, vision: VisionEngine = None) -> tuple[int, int, int]:
    """
    Trích xuất chỉ số tài nguyên hiện tại từ giả lập.
    Được thiết kế siêu nhẹ, loại bỏ PyTorch/EasyOCR bằng thuật toán khớp mẫu chữ số.
    """
    if adb is None:
        adb = ADBHelper()
    if vision is None:
        vision = VisionEngine()

    print("[SCOUT] Đang chụp ảnh màn hình để phân tích tài nguyên...")
    screenshot = adb.take_screenshot()
    if screenshot is None:
        print("[SCOUT ERROR] Không thể chụp ảnh màn hình từ giả lập.")
        return 0, 0, 0

    h_img, w_img, _ = screenshot.shape
    results = {}

    for label, (x1, y1, x2, y2) in COORDS.items():
        p = PADDING[label]
        
        # Tính toán tọa độ cắt bù trừ an toàn
        x1p = max(0, x1 - p["l"])
        y1p = max(0, y1 - p["t"])
        x2p = min(w_img, x2 + p["r"])
        y2p = min(h_img, y2 + p["b"])
        
        # Cắt ROI tài nguyên và nhận dạng chữ số qua VisionEngine
        roi = (x1p, y1p, x2p - x1p, y2p - y1p)
        val = vision.extract_numerical_metrics(screenshot, roi)
        results[label] = val

    gold = results.get("Gold", 0)
    elixir = results.get("Elixir", 0)
    dark_elixir = results.get("Dark Elixir", 0)

    print(f"[SCOUT] Kết quả quét -> Vàng: {gold:,} | Dầu hồng: {elixir:,} | Dầu đen: {dark_elixir:,}")
    return gold, elixir, dark_elixir

if __name__ == "__main__":
    # Điểm chạy độc lập để lập trình viên kiểm thử nhanh
    adb_instance = ADBHelper()
    vision_instance = VisionEngine()
    extract_resources(adb_instance, vision_instance)
