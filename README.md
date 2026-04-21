# GTTOD, unfucked

This is a BepInEx mod for Get to the Orange Door that replaces all stock character controller code with a custom, 
unified implementation. This mod was written in the interest of improving the game and to test what I've been
experimenting with in an actual in-game setting.

## A smoother GTTOD chracter controller

The stock character controller in GTTOD is implemented using rigidbody physics. Friciton, drag, and mass calculations 
performed by Unity rigidbodies tend to crush any amount of nuanced control you have over how exactly they move, no
matter how deliberate you are with your implementation. GTTOD's controller attempts to skirt around this issue
by selectively enabling/disabling kinematic mode on the player's rigidbody depending on the movement state, as well
as having an entirely separate controller just for handling wallruns.

To fix these issues, I've completely bypassed all stock movement code belonging to doorguy and replaced it with
my own discrete kinematic controller.

## what it do:
- Reworks wallrunning to be significantly more predictable
- Makes it easier to retain velocity after airdashing
- Allows the controller to predict when wallruns are about to happen to tilt the camera ahead of time
- Fixes lots of jitter issues caused by rigidbody/velocity fighting
- Re-implements gamefeel stuff to be easier on the eyes (less camera/weapon sway overall)


## whats left (todo)
- Actually implement the wallrun step (the code only contains the prediction stuff right now)
- Implement vaulting with a new timing system so that accidental vaults don't consume all your speed
- Reduce the height of the ground snap code and write a dedicated step detection algorithm
- Centralize important movement variables so that they can be adjusted via commands and/or the plugin config file
- Poke around more in the ac_CharacterController class to restore some functions that are probably broken with this mod active
