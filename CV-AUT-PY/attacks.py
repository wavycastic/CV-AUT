import time
import random
import cv2
import numpy as np
from adb_helper import ADBHelper
from vision_engine import VisionEngine
from interaction_executor import InteractionSequenceExecutor

class AttackExecutor:
    """
    Object-oriented executor for complex interaction sequences (battle sequences).
    Optimized for minimal CPU overhead, low latency, and realistic human-like inputs.
    """
    SCREEN_WIDTH = 1600
    MATCH_THRESHOLD = 0.52

    # Coordinate presets for troop/spell deployment
    # Left-side coordinates
    DRAGON_L = [
        (170, 384), (214, 348), (246, 327), (270, 306), (305, 285), (345, 255),
        (368, 238), (396, 216), (417, 201), (442, 182), (487, 152), (535, 121),
        (640, 35),  (442, 182)
    ]
    BALLOON_L = [
        (170, 384), (214, 348), (246, 327), (270, 306), (305, 285), (345, 255),
        (368, 238), (396, 216), (417, 201), (444, 183), (486, 154), (534, 122),
        (345, 255), (444, 183), (368, 238), (246, 327), (417, 201)
    ]
    RAGE_L = [(549, 353), (674, 247), (797, 317), (690, 439), (777, 403)]
    FREEZE_L = [(614, 371), (769, 276), (770, 363), (704, 494), (798, 405), (874, 405)]
    
    HERO_L = [
        {"name": "siege_machine", "coord": (364, 236)},
        {"name": "Queen",         "coord": (364, 236)},
        {"name": "BK",            "coord": (513, 135)},
        {"name": "Warden",        "coord": (445, 191)},
        {"name": "Prince",        "coord": (445, 191)},
        {"name": "RC",            "coord": (426, 204)}
    ]

    # Right-side coordinates (mirroring Left-side against 1600px width)
    DRAGON_R = [
        (1344, 346), (1272, 295), (1234, 261), (1191, 229), (1150, 200), (1116, 173),
        (1074, 138), (1042, 114), (1000, 91),  (946, 47),  (904, 18),  (1033, 108),
        (1091, 152), (1109, 172)
    ]
    BALLOON_R = DRAGON_R.copy() + [(1207, 209), (1296, 273), (1311, 256)]
    
    # Event Troop Mappings
    E_DRAGON_L = DRAGON_L[2:12]
    E_DRAGON_R = DRAGON_R[2:12]
    
    ICE_MINION_L = DRAGON_L[2:12]
    ICE_MINION_R = DRAGON_R[2:12]
    
    ICE_GOLEM_L = DRAGON_L[4:9]
    ICE_GOLEM_R = DRAGON_R[4:9]

    def __init__(self, adb_helper: ADBHelper, vision_engine: VisionEngine, executor: InteractionSequenceExecutor):
        self.adb = adb_helper
        self.vision = vision_engine
        self.executor = executor
        
        # Current active tabs coordinates on UI
        self.tabs = {}
        # Current selected deployment side
        self.side = "left"
        self.deploy_coords = {}

        # Set up dynamic coordinate patterns
        self._initialize_patterns()

    def _initialize_patterns(self):
        # Mirror RAGE and FREEZE for the right side
        rage_r = [(self.SCREEN_WIDTH - 1 - x, y) for x, y in self.RAGE_L]
        freeze_r = [(self.SCREEN_WIDTH - 1 - x, y) for x, y in self.FREEZE_L]
        
        # Mirror heroes
        hero_r = [
            {"name": h["name"], "coord": (self.SCREEN_WIDTH - 1 - h["coord"][0], h["coord"][1])}
            for h in self.HERO_L
        ]
        
        # Event Azure Dragon
        azure_dragon_l = [self.HERO_L[2]["coord"]] # Warden coordinate
        azure_dragon_r = [hero_r[2]["coord"]]

        self.patterns = {
            "left": {
                "dragon":        self.DRAGON_L,
                "e_drag":        self.E_DRAGON_L,
                "balloon":       self.BALLOON_L,
                "heroes":        self.HERO_L,
                "rage":          self.RAGE_L,
                "freeze":        self.FREEZE_L,
                "azure_dragon":  azure_dragon_l,
                "ice_minion":    self.ICE_MINION_L,
                "ice_golem":     self.ICE_GOLEM_L,
            },
            "right": {
                "dragon":        self.DRAGON_R,
                "e_drag":        self.E_DRAGON_R,
                "balloon":       self.BALLOON_R,
                "heroes":        hero_r,
                "rage":          rage_r,
                "freeze":        freeze_r,
                "azure_dragon":  azure_dragon_r,
                "ice_minion":    self.ICE_MINION_R,
                "ice_golem":     self.ICE_GOLEM_R,
            }
        }

    def update_tabs(self) -> dict:
        """
        Scans the screen to find active troop/spell/hero tabs at the bottom.
        Saves center coordinates for each detected tab.
        """
        print("[ATTACK] Scanning screen for active deployment tabs...")
        screenshot = self.adb.take_screenshot()
        if screenshot is None:
            return {}

        self.tabs = {}
        
        # Define category mappings for restructured Templates folder
        categories = {
            "dragon":        "troops/dragon",
            "e_drag":        "troops/E_Drag",
            "balloon":       "troops/balloon",
            "event_goblin":  "troops/event_goblin",
            "azure_dragon":  "troops/azure_dragon",
            "ice_minion":    "troops/ice_minion",
            "ice_golem":     "troops/ice_golem",
            "rage":          "spells/rage",
            "freeze":        "spells/freeze",
            "queen":         "heroes/queen",
            "bk":            "heroes/bk",
            "warden":        "heroes/warden",
            "prince":        "heroes/prince",
            "rc":            "heroes/rc"
        }

        # Scan each tab using structured subfolders
        for name, relative_path in categories.items():
            coord = self.vision.find_element(screenshot, relative_path, threshold=self.MATCH_THRESHOLD)
            if coord:
                self.tabs[name] = coord
                print(f"[ATTACK] Detected tab '{name}' at: {coord}")

        # Determine best siege machine template
        swt_score = self.vision.find_element(screenshot, "troops/siege_with_troops", threshold=self.MATCH_THRESHOLD)
        es_score = self.vision.find_element(screenshot, "troops/empty_siege", threshold=self.MATCH_THRESHOLD)
        
        if swt_score:
            self.tabs["siege_machine"] = swt_score
            print(f"[ATTACK] Detected siege machine (with troops) at: {swt_score}")
        elif es_score:
            self.tabs["siege_machine"] = es_score
            print(f"[ATTACK] Detected empty siege machine at: {es_score}")

        return self.tabs

    def _jitter_coord(self, x: int, y: int) -> tuple[int, int]:
        """Adds randomized human-like jitter offsets based on deployment sides to prevent detection."""
        if self.side == "left":
            dx = random.randint(0, 39)
            dy = random.randint(-27, 0)
        elif self.side == "right":
            dx = random.randint(-44, 0)
            dy = random.randint(-33, 0)
        else:
            dx = random.randint(-10, 10)
            dy = random.randint(-10, 10)
        return x + dx, y + dy

    def deploy_troops(self, troop_key: str):
        """Selects a troop tab and executes sequential taps on screen with anti-detection jitter."""
        tab = self.tabs.get(troop_key.lower())
        if not tab:
            print(f"[ATTACK] Skip: Tab for '{troop_key}' not found.")
            return

        coords = self.deploy_coords.get(troop_key.lower(), [])
        if not coords:
            print(f"[ATTACK] Skip: No coordinate patterns for '{troop_key}'.")
            return

        print(f"[ATTACK] Selecting tab '{troop_key}' -> Deploying ({len(coords)} taps)...")
        self.executor.click(tab[0], tab[1])
        time.sleep(0.5)
        
        # Apply jitter to coordinates
        jittered_coords = [self._jitter_coord(cx, cy) for cx, cy in coords]
        self.executor.execute_sequence(jittered_coords)

    def deploy_heroes(self):
        """Deploys heroes on screen by selecting tabs and clicking coordinates with anti-detection jitter."""
        print("[ATTACK] Initiating hero deployment...")
        heroes = self.deploy_coords.get("heroes", [])
        for hero in heroes:
            tab = self.tabs.get(hero["name"].lower())
            if not tab:
                print(f"[ATTACK] Skip Hero: Tab for '{hero['name']}' not found.")
                continue

            self.executor.click(tab[0], tab[1])
            time.sleep(0.1)
            jx, jy = self._jitter_coord(hero["coord"][0], hero["coord"][1])
            self.executor.click(jx, jy)

    def deploy_spells(self, spell_key: str):
        """Selects a spell and deploys it on coordinate positions with adaptive delays and anti-detection jitter."""
        tab = self.tabs.get(spell_key.lower())
        if not spell_key:
            print(f"[ATTACK] Skip Spell: Tab for '{spell_key}' not found.")
            return

        coords = self.deploy_coords.get(spell_key.lower(), [])
        print(f"[ATTACK] Selecting spell '{spell_key}' -> Casting ({len(coords)} spells)...")
        self.executor.click(tab[0], tab[1])
        
        delay = 1.0 if spell_key.lower() == "rage" else 2.0
        for x, y in coords:
            time.sleep(delay)
            jx, jy = self._jitter_coord(x, y)
            self.executor.click(jx, jy)

    def retap_heroes(self):
        """Continuously retaps hero abilities to trigger active skills during fight."""
        print("[ATTACK] Activating hero abilities...")
        for tag in ["warden", "queen", "bk", "prince", "rc"]:
            tab = self.tabs.get(tag)
            if tab:
                self.executor.click(tab[0], tab[1])

    def execute_dragon_sequence(self):
        """Standard Dragon + Balloon testing sequence."""
        self.deploy_troops("dragon")
        self.deploy_troops("ice_minion")
        self.deploy_troops("ice_golem")
        self.deploy_troops("azure_dragon")
        
        if "event_goblin" in self.tabs:
            tab = self.tabs["event_goblin"]
            self.executor.click(tab[0], tab[1])
            goblin_coords = self.deploy_coords.get("dragon", [])[:10]
            self.executor.execute_sequence(goblin_coords)

        self.deploy_troops("balloon")
        self.deploy_troops("siege_machine")
        self.deploy_heroes()
        self.deploy_spells("rage")
        print("[ATTACK] Waiting before freeze deployment...")
        self.deploy_spells("freeze")

    def execute_electro_dragon_sequence(self):
        """Standard ElectroDragon + Balloon testing sequence."""
        self.deploy_troops("e_drag")
        self.deploy_troops("ice_minion")
        self.deploy_troops("ice_golem")
        self.deploy_troops("azure_dragon")
        
        if "event_goblin" in self.tabs:
            tab = self.tabs["event_goblin"]
            self.executor.click(tab[0], tab[1])
            goblin_coords = self.deploy_coords.get("dragon", [])[:10]
            self.executor.execute_sequence(goblin_coords)

        self.deploy_troops("balloon")
        self.deploy_troops("siege_machine")
        self.deploy_heroes()
        self.deploy_spells("rage")
        print("[ATTACK] Waiting before freeze deployment...")
        self.deploy_spells("freeze")

    def run(self, attack_strategy: str = "Dragon_Attack"):
        """Main orchestrator entry point called by the FSM."""
        self.side = random.choice(["left", "right"])
        self.deploy_coords = self.patterns[self.side]
        
        print(f"\n==============================================")
        print(f"[ATTACK] Executing: {attack_strategy} | Side: {self.side.upper()}")
        print(f"==============================================")

        self.update_tabs()

        if attack_strategy == "Dragon_Attack":
            self.execute_dragon_sequence()
        elif attack_strategy == "ElectroDragon_Attack":
            self.execute_electro_dragon_sequence()
        else:
            print(f"[ATTACK ERROR] Unknown strategy: {attack_strategy}")
            return

        self.retap_heroes()
        print("[ATTACK] Sequence completed successfully.")
