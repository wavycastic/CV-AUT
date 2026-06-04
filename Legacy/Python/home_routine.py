import subprocess
import time
import os
import sys
from adb_helper import ADBHelper
from vision_engine import VisionEngine

class HomeRoutine:
    """
    Handles standard active base routines: ensuring base focus,
    restarting emulator game instance (boot recovery), zooming out, harvesting collectors,
    and switching between multiple Supercell ID accounts.
    """
    def __init__(self, adb: ADBHelper, vision: VisionEngine):
        self.adb = adb
        self.vision = vision
        self.create_no_window = subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0
        
        # Standard Supercell ID accounts list click coordinates (on standard 1600x900 resolution)
        # Binned and validated from original ready_villages.pyc constants
        self.village_coords = {
            1: (1088, 212),
            2: (1088, 379),
            3: (1088, 532),
            4: (1088, 702),
            5: (1088, 858)
        }

    def boot_recovery(self) -> None:
        """
        Force stops and restarts Clash of Clans on the emulator, dismissing popup ads.
        Equivalent to the original boot_recovery.pyc sequence.
        """
        host = self.adb.device_address
        if not host:
            raise RuntimeError("No emulator address selected for boot_recovery()")
        
        print("🔁 [RECOVERY] Restarting Clash of Clans...")
        # 1. Force stop game process
        subprocess.run(
            [self.adb.adb_bin, "-s", host, "shell", "am", "force-stop", "com.supercell.clashofclans"],
            creationflags=self.create_no_window,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL
        )
        
        # 2. Launch game using monkey (guarantees launching launcher category cleanly)
        subprocess.run(
            [self.adb.adb_bin, "-s", host, "shell", "monkey", "-p", "com.supercell.clashofclans", "-c", "android.intent.category.LAUNCHER", "1"],
            creationflags=self.create_no_window,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL
        )
        
        print("⏳ [RECOVERY] Waiting 10 seconds for game to load...")
        time.sleep(10)
        
        print("👆 [RECOVERY] Dismissing initial pop-ups...")
        # Simulates clicking at (146, 487) to close normal login popups/ads
        self.adb.tap(146, 487)
        time.sleep(1)

    def ensure_home_base(self, max_wait: int = 50) -> bool:
        """
        Verifies if the bot is on the main village base, reboots if timed out.
        Reconstructed from the original ensure_home_base.pyc.
        """
        print("🧠 [BASE] Checking if we're on the home base screen...")
        start_time = time.time()
        
        while time.time() - start_time < max_wait:
            screenshot = self.adb.take_screenshot()
            if screenshot is not None:
                # detect_home_base equivalent: check for ui/home_page
                base_found = self.vision.find_element(screenshot, "ui/home_page", threshold=0.7)
                if base_found:
                    print("✅ [BASE] Home base confirmed.")
                    return True
            
            print("⏳ [BASE] Still not on home base. Retrying in 5s...")
            time.sleep(5)
            
        print("❌ [BASE] Failed to detect home base. Initiating reboot sequence...")
        self.boot_recovery()
        # Recursive verification with lower timeout
        return self.ensure_home_base(max_wait=20)

    def multi_zoom_out(self) -> None:
        """
        Executes the local zoom_out.ahk script to zoom out the MEmu camera without stealing focus.
        Optimized Win32 thread message sender.
        """
        print("🔍 [ZOOM] Executing AutoHotkey zoom out...")
        base_dir = os.path.dirname(os.path.abspath(__file__))
        ahk_exe = os.path.join(base_dir, "AutoHotkey64.exe")
        ahk_script = os.path.join(base_dir, "zoom_out.ahk")
        
        if os.path.exists(ahk_exe) and os.path.exists(ahk_script):
            try:
                subprocess.Popen(
                    [ahk_exe, ahk_script],
                    creationflags=self.create_no_window
                )
                print("✅ [ZOOM] AHK Zoom-out script triggered successfully.")
            except Exception as e:
                print(f"[ZOOM ERROR] Failed to run AutoHotkey: {e}")
        else:
            print("[ZOOM WARNING] AutoHotkey64.exe or zoom_out.ahk not found. Skipping AHK zoom.")

    def find_and_tap_collectors(self) -> None:
        """
        Scans the screen and taps resource collectors if templates are found.
        Equivalent to original Tap_Collectors.pyc.
        """
        print("[COLLECTORS] Scanning and harvesting mines/collectors...")
        screenshot = self.adb.take_screenshot()
        if screenshot is None:
            return
            
        # Scan for gold/elixir collectors template
        collector_pos = self.vision.find_element(screenshot, "ui/collectors", threshold=0.65)
        if collector_pos:
            print(f"💰 [COLLECTORS] Harvesting collectors at {collector_pos}")
            self.adb.tap(collector_pos[0], collector_pos[1])
        else:
            print("[COLLECTORS] No collectors detected or templates missing.")

    def switch_to_village(self, idx: int) -> bool:
        """
        Switches between different accounts using the game's Supercell ID list interface.
        Reconstructed and optimized from the original ready_villages.pyc.
        """
        print(f"🔄 [SWITCH] Switching to Village_{idx}...")
        
        # 1. Tap the Settings Gear icon (at 1534, 649 on standard base)
        self.adb.tap(1534, 649)
        time.sleep(2)
        
        # 2. Capture screen and match the "Switch Account" button
        screenshot = self.adb.take_screenshot()
        if screenshot is None:
            print("[SWITCH ERROR] Failed to capture screen to locate Switch button.")
            return False
            
        # Search "Switch Account" button in ROI (562, 110, 1333, 282)
        sw_btn = self.vision.find_element(screenshot, "ui/switch_button", threshold=0.7)
        if not sw_btn:
            print("[SWITCH WARN] 'Switch Account' button not found. Closing settings panel...")
            self.adb.tap(1288, 96) # Tap Close settings button (typically at 1288, 96)
            return False
            
        # 3. Tap the Switch Account button
        self.adb.tap(sw_btn[0], sw_btn[1])
        time.sleep(3) # Wait for Supercell ID accounts list to load
        
        # 4. Tap the target village coordinate from mapping
        coord = self.village_coords.get(idx)
        if not coord:
            print(f"[SWITCH ERROR] Invalid village index: {idx}")
            # Exit list UI by clicking top-left exit (typically at 52, 59)
            self.adb.tap(52, 59)
            return False
            
        print(f"[SWITCH] Selecting village slot coordinate {coord}...")
        self.adb.tap(coord[0], coord[1])
        time.sleep(6) # Wait for Clash of Clans to reload and render the new village
        
        print(f"✅ [SWITCH] Switched to Village_{idx} successfully.")
        return True
