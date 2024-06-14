# Steam Deck Tools minimal for Windows

The goal of this project is to build a miminal controller support for windows on steam deck. And remove all unnecessary depenencies.
This tool is a slim version of the steam deck tools. I removed all other tools, keep only the controller.
Also removed all unnecessary nugets, auto updater, dlls. For the required two dll dependencies, I rebuilt both from the source code.
But it still depends on the ViGEmBus, and a couple nugets, which I reviewed the source code, should be safe, but I didn't rebuit them.
