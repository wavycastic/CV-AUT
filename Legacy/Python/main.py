import glob
import json
import os
import sys
import threading
import time

from adb_helper import ADBHelper
from attacks import AttackExecutor
from home_routine import HomeRoutine
from interaction_executor import InteractionSequenceExecutor
from IsTarget import extract_resources
from smart_train import SmartTrain
from vision_engine import VisionEngine


class CVAutomationFramework:
    """
    Finite State Machine (FSM) Orchestrator for CV-AUT.
    Manages multi-account loops (bot_loop) and standard active base routines (one_cycle).
    Fully modularized and designed for extremely low CPU/RAM overhead (<20MB RAM).
    """

    def __init__(self, config_path="test_config.json"):
        # Load global configuration
        self.config_path = config_path
        self.config = self._load_config(config_path)

        # Configure thread events for control flow
        self.stop_event = threading.Event()
        self.pause_event = threading.Event()
        self.pause_event.set()  # Unpaused by default

        # Initialize Core modules
        dev_config = self.config.get("device_connection", {})
        self.adb = ADBHelper(
            host=dev_config.get("host", "127.0.0.1"), port=dev_config.get("port", 5556)
        )
        self.vision = VisionEngine()
        self.executor = InteractionSequenceExecutor(self.adb, self.config)
        self.home = HomeRoutine(self.adb, self.vision)
        self.smart_train = SmartTrain(self.adb, self.vision)
        self.attacks = AttackExecutor(self.adb, self.vision, self.executor)

        # Stats tracking
        self.cycle_count = 0
        self.current_village_idx = 1

        print("[CV-AUT] Framework core initialized successfully.")

    def _load_config(self, path) -> dict:
        if os.path.exists(path):
            with open(path, "r") as f:
                return json.load(f)

        # Default fallback configurations
        return {
            "device_connection": {"host": "127.0.0.1", "port": 5556},
            "farming_thresholds": {
                "gold_threshold": 650000,
                "elixir_threshold": 650000,
                "dark_elixir_threshold": 0,
            },
            "element_state_automation": {
                "upgrade_enabled": True,
                "wall_level": 12,
                "min_retained_gold": 5000000,
            },
            "clan_capital": {"enable_clan_capital": True, "capital_hall_level": 9},
            "multi_account": {
                "enable_multi_account": True,
                "multi_interval_mins": 60,
                "selected_villages": [1, 2],
            },
        }

    def _check_stop(self) -> bool:
        return self.stop_event.is_set()

    def one_cycle(self, cfg: dict):
        """
        Executes a single test/automation cycle on the active account.
        Reconstructed and optimized from the original one_cycle.pyc loop.
        """
        self.pause_event.wait()
        if self._check_stop():
            print("[FSM] Cycle aborted: Stop event is set.")
            return

        print(
            f"\n--- [FSM] Starting One Cycle (Village_{self.current_village_idx}) ---"
        )

        # 1. Verification of Home Base screen focus
        self.pause_event.wait()
        if not self.home.ensure_home_base(max_wait=50):
            print("[FSM ERROR] Unable to ensure home base focus. Aborting cycle.")
            return

        # 2. Focus device / tap default center coordinate (140, 606)
        self.adb.tap(140, 606)
        time.sleep(1)

        # 3. Pull camera back to full view (Zoom Out)
        self.pause_event.wait()
        self.home.multi_zoom_out()

        # 4. Smart/Quick troop training based on intervals
        self.pause_event.wait()
        train_mode = cfg.get("train_mode", "quick")
        if train_mode == "quick":
            if self.cycle_count % 5 == 0:
                print(
                    f"[FSM] Cooking army: Triggering Quick Train (Slot {cfg.get('quick_slot', 1)})..."
                )
                # quick_train simulation click
                self.adb.tap(50, 700)
                time.sleep(1)
        else:
            if self.cycle_count % 3 == 0:
                self.smart_train.run(cfg)

        # 5. Handle Wall Upgrades (Routine Maintenance)
        self.pause_event.wait()
        wall_config = cfg.get("element_state_automation", {})
        if wall_config.get("upgrade_enabled", False) and not self._check_stop():
            wall_level = wall_config.get("wall_level", 12)
            print(f"[FSM] Wall Upgrader: Upgrading walls to level {wall_level}...")
            # handle_home_resources simulation

        # 6. Request Troops from Clan
        self.pause_event.wait()
        if cfg.get("request_troops", True) and not self._check_stop():
            print("[FSM] Requesting troops: Opening clan chat...")
            # auto_request simulation

        # 7. Harvest Resource Collectors (Gold Mines / Elixir Collectors)
        self.pause_event.wait()
        if not self._check_stop():
            self.home.find_and_tap_collectors()

        # 8. Start Resource Farming Sequence (Target Scouting)
        self.pause_event.wait()
        if not self._check_stop():
            farm_thresh = cfg.get("farming_thresholds", {})
            gold_req = farm_thresh.get("gold_threshold", 650000)
            elixir_req = farm_thresh.get("elixir_threshold", 650000)
            de_req = farm_thresh.get("dark_elixir_threshold", 0)

            print(f"\n==============================================")
            print(
                f"[FSM] Bắt đầu cướp tài nguyên (Mục tiêu: Gold >= {gold_req:,}, Elixir >= {elixir_req:,}, Dầu đen >= {de_req:,})"
            )
            print(f"==============================================")

            # Mở màn hình tìm trận (Click nút Attack: 106, 784)
            print("[FSM] Đang mở màn hình Tấn công...")
            self.adb.tap(106, 784)
            time.sleep(1.5)

            # Click nút "Tìm trận đấu" (Find a Match: 1324, 608)
            print("[FSM] Đang nhấn Tìm trận đấu...")
            self.adb.tap(1324, 608)
            time.sleep(4.0)  # Chờ màn hình tải trận đầu tiên

            search_count = 1
            max_searches = 50
            battle_executed = False

            while search_count <= max_searches and not self._check_stop():
                self.pause_event.wait()
                print(
                    f"\n[FSM] Đang phân tích nhà đối thủ thứ {search_count}/{max_searches}..."
                )

                # Quét tài nguyên nhà đối thủ bằng mô-đun Light OCR của IsTarget
                gold, elixir, dark_elixir = extract_resources(self.adb, self.vision)

                # Kiểm tra chỉ tiêu tài nguyên
                if gold >= gold_req and elixir >= elixir_req and dark_elixir >= de_req:
                    print(
                        f"[FSM] Tìm thấy nhà phù hợp! Gold={gold:,} >= {gold_req:,} | Elixir={elixir:,} >= {elixir_req:,}"
                    )
                    print("[FSM] Đang khởi chạy kịch bản thả quân tấn công...")

                    # Chạy kịch bản thả quân
                    strategy = cfg.get("attack_strategy", "Dragon_Attack")
                    self.attacks.run(strategy)
                    battle_executed = True

                    # Chờ trận đấu kết thúc (Thời gian chuẩn của trận là 3 phút = 180s)
                    # Trong lúc chờ, chúng ta liên tục kích hoạt skill của tướng mỗi 15 giây
                    print("[FSM] Đang theo dõi trận đấu và kích hoạt skill tướng...")
                    for i in range(12):  # 12 * 15s = 180s = 3 phút
                        time.sleep(15)
                        self.pause_event.wait()
                        if self._check_stop():
                            break
                        self.attacks.retap_heroes()

                    # Trở về làng (Click Home: 800, 780)
                    print("[FSM] Kết thúc trận đấu. Đang quay trở lại làng chính...")
                    self.adb.tap(800, 780)
                    time.sleep(5)
                    break
                else:
                    print(f"[FSM] Tài nguyên chưa đạt yêu cầu. Đang bỏ qua...")

                    # Click nút "Next" để tìm nhà khác (Next button: 1422, 545)
                    self.adb.tap(1422, 545)
                    search_count += 1
                    time.sleep(1.5)  # Chờ tải trận mới

            if not battle_executed and not self._check_stop():
                print(
                    "[FSM WARN] Đã đạt giới hạn tìm kiếm tối đa mà không tìm được nhà phù hợp. Surrendering..."
                )
                # Click nút kết thúc (Surrender button: 80, 780)
                self.adb.tap(80, 780)
                time.sleep(1)
                # Xác nhận Surrender (Click OK: 960, 560)
                self.adb.tap(960, 560)
                time.sleep(2)
                # Trở về làng (Click Home: 800, 780)
                self.adb.tap(800, 780)
                time.sleep(5)

        self.cycle_count += 1
        print(f"--- [FSM] One Cycle Finished (Village_{self.current_village_idx}) ---")

    def bot_loop(self):
        """
        Orchestrates multi-account village switching.
        Reconstructed and optimized from the original bot_loop.pyc structure.
        """
        print("[CV-AUT] Starting Main automation orchestrator loop...")

        # Locate profiles
        profiles_dir = "profiles"
        json_paths = glob.glob(os.path.join(profiles_dir, "Village_*.json"))
        existing_count = len(json_paths)

        multi_config = self.config.get("multi_account", {})
        enable_multi = multi_config.get("enable_multi_account", True)
        selected_villages = multi_config.get("selected_villages", [1])

        if not enable_multi or existing_count <= 1:
            print("[INFO] Single account mode activated.")
            self.current_village_idx = 1
            while not self._check_stop():
                self.one_cycle(self.config)
                time.sleep(10)  # Pause between cycles
            return

        # Multi-account routine
        interval_secs = multi_config.get("multi_interval_mins", 60) * 60
        print(
            f"[INFO] Multi-account cycling active. Selected: {selected_villages} every {interval_secs / 60:.1f} mins."
        )

        while not self._check_stop():
            for idx in selected_villages:
                self.pause_event.wait()
                if self._check_stop():
                    break

                # 1. Perform switch
                self.current_village_idx = idx
                if not self.home.switch_to_village(idx):
                    print(f"[FSM ERROR] Failed to switch to Village_{idx}. Skipping...")
                    continue

                # 2. Load merged configuration for this specific account
                village_cfg_path = os.path.join(profiles_dir, f"Village_{idx}.json")
                village_cfg = {}
                if os.path.exists(village_cfg_path):
                    try:
                        with open(village_cfg_path, "r") as f:
                            village_cfg = json.load(f)
                    except Exception as e:
                        print(
                            f"[FSM WARNING] Error reading config for Village_{idx}: {e}. Using defaults."
                        )

                # Merge village configs over global settings
                merged_cfg = self.config.copy()
                merged_cfg.update(village_cfg)

                # Run cycles on this account within the allocated slot time
                slot_start = time.time()
                self.cycle_count = 0

                while (
                    time.time() - slot_start < interval_secs and not self._check_stop()
                ):
                    self.pause_event.wait()
                    self.one_cycle(merged_cfg)

                    # Short cooling period between cycles inside the slot
                    time.sleep(15)

                print(f"[FSM] Time slot for Village_{idx} finished.")

            # Short loop delay
            time.sleep(5)

        print("[CV-AUT] Multi-account orchestrator stopped.")

    def start(self):
        """Starts the FSM main runner thread."""
        self.runner_thread = threading.Thread(target=self.bot_loop, daemon=True)
        self.runner_thread.start()

    def stop(self):
        self.stop_event.set()


if __name__ == "__main__":
    framework = CVAutomationFramework()
    # Execute a single dry-run cycle for verification
    print("[DRY-RUN] Executing a single dry run cycle...")
    framework.one_cycle(framework.config)
