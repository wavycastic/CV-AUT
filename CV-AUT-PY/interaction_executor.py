import random
import time
from adb_helper import ADBHelper

class InteractionSequenceExecutor:
    """
    Simulates human-like inputs to test device interface stability.
    Uses Gaussian distribution for clicks and randomized delays to prevent programmatic patterns.
    """
    def __init__(self, adb_helper: ADBHelper, config: dict):
        self.adb = adb_helper
        self.config = config
        
        # Read parameters from config
        sim_config = config.get("interaction_simulation", {})
        self.jitter_sd = sim_config.get("jitter_standard_deviation_px", 5.0)
        self.min_delay = sim_config.get("min_delay_ms", 150) / 1000.0
        self.max_delay = sim_config.get("max_delay_ms", 450) / 1000.0

    def _sleep_adaptive_delay(self):
        """Pauses execution with a random delay between sequential operations."""
        sleep_time = random.uniform(self.min_delay, self.max_delay)
        time.sleep(sleep_time)

    def _apply_jitter(self, x: int, y: int) -> tuple[int, int]:
        """
        Applies a normal (Gaussian) distribution offset to coordinates.
        Concentrates clicks at the center, matching real human finger presses.
        """
        jitter_x = int(random.gauss(x, self.jitter_sd))
        jitter_y = int(random.gauss(y, self.jitter_sd))
        return jitter_x, jitter_y

    def click(self, x: int, y: int):
        """Executes a humanized single click at target coordinates."""
        jx, jy = self._apply_jitter(x, y)
        self.adb.tap(jx, jy)
        self._sleep_adaptive_delay()

    def execute_sequence(self, coordinates: list[tuple[int, int]]):
        """Simulates a sequence of rapid taps (e.g. troop deployment grid)."""
        for x, y in coordinates:
            self.click(x, y)

    def swipe_humanized(self, x1: int, y1: int, x2: int, y2: int):
        """Executes a swipe with slight coordinate jittering."""
        jx1, jy1 = self._apply_jitter(x1, y1)
        jx2, jy2 = self._apply_jitter(x2, y2)
        duration = random.randint(250, 400)
        self.adb.swipe(jx1, jy1, jx2, jy2, duration)
        self._sleep_adaptive_delay()
