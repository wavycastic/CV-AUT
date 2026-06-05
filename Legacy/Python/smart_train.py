import time
import os
import sys
from adb_helper import ADBHelper
from vision_engine import VisionEngine

class SmartTrain:
    """
    Handles automatic troop validation and training sequences.
    Reconstructed and optimized from the original smart_train.pyc.
    Supports fixed-coordinate clicks and template-matching validation for TH11-TH13 Dragon Attack composition.
    """
    def __init__(self, adb: ADBHelper, vision: VisionEngine):
        self.adb = adb
        self.vision = vision
        
        # ADB Clicks - Standard coordinates at 1600x900 resolution
        self.OPEN_ARMY_WINDOW = (62, 658)
        self.CLOSE_ARMY_WINDOW = (1545, 81)
        
        # Tabs inside the Army window
        self.OPEN_ARMY_TAB = (1063, 305)
        self.CLOSE_ARMY_TAB = (47, 85)
        self.OPEN_SPELL_TAB = (1008, 531)
        self.CLOSE_SPELL_TAB = (59, 52)
        self.OPEN_SIEGE_TAB = (1398, 533)
        self.CLOSE_SIEGE_TAB = (27, 85)
        
        # Trash & Clear coordinates
        self.TAP_CLEAR_ARMY = (1546, 209)
        self.CONFIRM_TAP_ARMY = (969, 579)
        self.TAP_CLEAR_SPELL = (1225, 429)
        self.CONFIRM_TAP_SPELL = (978, 583)
        self.TAP_CLEAR_SIEGE = (1545, 427)
        self.CONFIRM_TAP_SIEGE = (966, 581)

        # Simulated troop compositions (Dragon + Balloon)
        # Spaces: Dragon = 20, Balloon = 5
        self.troop_space_costs = {"dragon": 20, "balloon": 5}
        self.spell_space_costs = {"rage": 2, "freeze": 1}

    def validate_army_window(self) -> bool:
        """
        Clicks to open the army window and verifies if it is loaded.
        Reconstructed from original _validate_army_window.
        """
        print("[SMART] Opening Army Window...")
        self.adb.tap(self.OPEN_ARMY_WINDOW[0], self.OPEN_ARMY_WINDOW[1])
        time.sleep(1.5)
        
        screenshot = self.adb.take_screenshot()
        if screenshot is None:
            print("[SMART ERROR] Unable to capture screen for army window validation.")
            return False
            
        # Verify army window by checking for a specific HUD template in top-left
        # Standard ROI: (76, 57, 565, 156) matches army_window.png template
        window_found = self.vision.find_element(screenshot, "ui/army_window_crop", threshold=0.6)
        if window_found:
            print("[SMART] Army window successfully verified.")
            return True
            
        print("[SMART WARN] Army window not detected.")
        return False

    def validate_troops(self, cfg: dict) -> bool:
        """Checks if current troop counts are correct. Returns True if composition is valid."""
        print("[SMART] Validating current troops...")
        screenshot = self.adb.take_screenshot()
        if screenshot is None:
            return False
            
        # Matches templates/ui/army_window_crop or templates/army/dragon.png
        # In a dry-run or mock mode, we assume validation checks pass if screenshot matches typical base
        dragon_ok = self.vision.find_element(screenshot, "troops/dragon", threshold=0.7)
        balloon_ok = self.vision.find_element(screenshot, "troops/balloon", threshold=0.7)
        
        if dragon_ok and balloon_ok:
            print("[SMART] Troops validation passed.")
            return True
            
        print("[SMART WARN] Missing main troops. Retraining required.")
        return False

    def validate_spells(self) -> bool:
        """Checks if current spell counts are correct."""
        print("[SMART] Validating spells...")
        screenshot = self.adb.take_screenshot()
        if screenshot is None:
            return False
            
        rage_ok = self.vision.find_element(screenshot, "spells/rage", threshold=0.7)
        freeze_ok = self.vision.find_element(screenshot, "spells/freeze", threshold=0.7)
        
        if rage_ok and freeze_ok:
            print("[SMART] Spells validation passed.")
            return True
            
        print("[SMART WARN] Spells incomplete. Production required.")
        return False

    def validate_siege(self) -> bool:
        """Checks if siege machine is ready."""
        print("[SMART] Validating siege machines...")
        screenshot = self.adb.take_screenshot()
        if screenshot is None:
            return False
            
        slammer_ok = self.vision.find_element(screenshot, "ui/treasure_event", threshold=0.6) # placeholder template matching
        if slammer_ok:
            print("[SMART] Siege machine confirmed ready.")
            return True
            
        print("[SMART WARN] Stone Slammer missing.")
        return False

    def train_troops(self, cfg: dict) -> None:
        """Executes troop training clicks."""
        print("[TRAIN] Initiating fresh troop training load...")
        
        # 1. Clear old incorrect troops in queue (Trash)
        self.adb.tap(self.TAP_CLEAR_ARMY[0], self.TAP_CLEAR_ARMY[1])
        time.sleep(0.5)
        self.adb.tap(self.CONFIRM_TAP_ARMY[0], self.CONFIRM_TAP_ARMY[1])
        time.sleep(1)
        
        # 2. Open troops training tab
        self.adb.tap(self.OPEN_ARMY_TAB[0], self.OPEN_ARMY_TAB[1])
        time.sleep(1)
        
        # 3. Simulate taps on troops templates
        # Typically trains 9x Dragons and 7x Balloons for TH11
        print("[TRAIN] Producing 9x Dragons...")
        for _ in range(9):
            self.adb.tap(500, 500) # Click Dragon icon coordinates in tab
            time.sleep(0.1)
            
        print("[TRAIN] Producing 7x Balloons...")
        for _ in range(7):
            self.adb.tap(600, 500) # Click Balloon icon coordinates in tab
            time.sleep(0.1)
            
        # 4. Close tab
        self.adb.tap(self.CLOSE_ARMY_TAB[0], self.CLOSE_ARMY_TAB[1])
        time.sleep(1)
        print("[TRAIN] Troops queued successfully.")

    def train_spells(self, cfg: dict) -> None:
        """Executes spell training clicks."""
        print("[TRAIN] Initiating spell production load...")
        
        # 1. Clear old spells (Trash)
        self.adb.tap(self.TAP_CLEAR_SPELL[0], self.TAP_CLEAR_SPELL[1])
        time.sleep(0.5)
        self.adb.tap(self.CONFIRM_TAP_SPELL[0], self.CONFIRM_TAP_SPELL[1])
        time.sleep(1)
        
        # 2. Open spells tab
        self.adb.tap(self.OPEN_SPELL_TAB[0], self.OPEN_SPELL_TAB[1])
        time.sleep(1)
        
        # 3. Queue Spells (3x Rage, 5x Freeze for TH11 standard)
        print("[TRAIN] Producing 3x Rage spells...")
        for _ in range(3):
            self.adb.tap(500, 650) # Simulate click Rage icon
            time.sleep(0.1)
            
        print("[TRAIN] Producing 5x Freeze spells...")
        for _ in range(5):
            self.adb.tap(600, 650) # Simulate click Freeze icon
            time.sleep(0.1)
            
        # 4. Close spells tab
        self.adb.tap(self.CLOSE_SPELL_TAB[0], self.CLOSE_SPELL_TAB[1])
        time.sleep(1)
        print("[TRAIN] Spells queued successfully.")

    def train_slammer(self, cfg: dict) -> None:
        """Produces siege machine."""
        print("[TRAIN] Queuing Stone Slammer production...")
        
        # 1. Clear queue
        self.adb.tap(self.TAP_CLEAR_SIEGE[0], self.TAP_CLEAR_SIEGE[1])
        time.sleep(0.5)
        self.adb.tap(self.CONFIRM_TAP_SIEGE[0], self.CONFIRM_TAP_SIEGE[1])
        time.sleep(1)
        
        # 2. Open siege tab
        self.adb.tap(self.OPEN_SIEGE_TAB[0], self.OPEN_SIEGE_TAB[1])
        time.sleep(1)
        
        # 3. Click Stone Slammer icon
        self.adb.tap(500, 750) # Click Stone Slammer
        time.sleep(0.5)
        
        # 4. Close siege tab
        self.adb.tap(self.CLOSE_SIEGE_TAB[0], self.CLOSE_SIEGE_TAB[1])
        time.sleep(1)
        print("[TRAIN] Stone Slammer queued.")

    def run(self, cfg: dict) -> None:
        """Runs the complete smart train loop."""
        print("\n--- [SMART] Starting Smart Train Sequence ---")
        if not self.validate_army_window():
            print("[SMART WARN] Smart train aborted: window not opened.")
            return
            
        # Check components
        troops_ok = self.validate_troops(cfg)
        spells_ok = self.validate_spells()
        siege_ok = self.validate_siege()
        
        if troops_ok and spells_ok and siege_ok:
            print("[SMART] Army composition correct. No training needed.")
        else:
            # Rebuild missing or incorrect items
            if not troops_ok:
                self.train_troops(cfg)
            if not spells_ok:
                self.train_spells(cfg)
            if not siege_ok:
                self.train_slammer(cfg)
                
        # Close the window cleanly
        print("[SMART] Closing Army Window...")
        self.adb.tap(self.CLOSE_ARMY_WINDOW[0], self.CLOSE_ARMY_WINDOW[1])
        time.sleep(1)
        print("--- [SMART] Smart Train Sequence Finished ---")
