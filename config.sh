# Settings for citavi-crossover. The defaults fit a standard installation, so most people never
# edit this file. Each value can also be set for one run from the environment, e.g.
#   CITAVI_BOTTLE="Citavi Test" ./citavi-crossover setup

# Name of the bottle Citavi lives in (created by `setup` if it does not exist).
BOTTLE_NAME="${CITAVI_BOTTLE:-Citavi 7}"

# CrossOver itself: looked for in /Applications, then ~/Applications, then anywhere Spotlight knows.
if [[ -n "${CITAVI_CROSSOVER_APP:-}" ]]; then CROSSOVER_APP="$CITAVI_CROSSOVER_APP"
elif [[ -d /Applications/CrossOver.app ]]; then CROSSOVER_APP=/Applications/CrossOver.app
elif [[ -d "$HOME/Applications/CrossOver.app" ]]; then CROSSOVER_APP="$HOME/Applications/CrossOver.app"
else
  CROSSOVER_APP=$(mdfind "kMDItemCFBundleIdentifier == 'com.codeweavers.CrossOver'" 2>/dev/null | head -1)
  CROSSOVER_APP="${CROSSOVER_APP:-/Applications/CrossOver.app}"
fi

# Wine from winehq.org, the alternative to CrossOver: the folder that holds its `wine` and
# `wineserver` programs. Looked for in the Wine Stable, Wine Devel and Wine Staging apps, then
# wherever a `wine` command is installed.
WINE_BIN="${CITAVI_WINE_BIN:-}"
if [[ -z "$WINE_BIN" ]]; then
  for _d in {/Applications,"$HOME/Applications"}/"Wine "{Stable,Devel,Staging}.app/Contents/Resources/wine/bin; do
    [[ -x "$_d/wine" ]] && { WINE_BIN=$_d; break; }
  done
  [[ -z "$WINE_BIN" ]] && _d=$(command -v wine 2>/dev/null) && WINE_BIN=${_d:A:h}
  unset _d
fi

# What runs Windows programs: crossover or wine. Left empty, it is CrossOver when CrossOver is
# installed, and Wine otherwise. A bottle belongs to the one that made it.
ENGINE="${CITAVI_ENGINE:-}"
if [[ -z "$ENGINE" ]]; then
  if [[ -d "$CROSSOVER_APP" || -z "$WINE_BIN" ]]; then ENGINE=crossover; else ENGINE=wine; fi
fi

# Folder that holds the bottles. With CrossOver: the one set in CrossOver's own settings, else
# CrossOver's standard folder. With Wine: a folder of this tool's own (a Wine bottle is what Wine
# calls a prefix).
BOTTLE_DIR="${CITAVI_BOTTLE_DIR:-}"
if [[ -z "$BOTTLE_DIR" && "$ENGINE" == wine ]]; then
  BOTTLE_DIR="$HOME/Library/Application Support/citavi-crossover/Bottles"
elif [[ -z "$BOTTLE_DIR" ]]; then
  BOTTLE_DIR=$(defaults read com.codeweavers.CrossOver BottleDir 2>/dev/null)
  BOTTLE_DIR="${BOTTLE_DIR:-$HOME/Library/Application Support/CrossOver/Bottles}"
fi

# Optional shortcut. If a bottle with this name exists and holds Microsoft .NET Framework 4.8 and
# nothing else, `setup` copies it instead of installing .NET again. Ignored if there is no such
# bottle. CrossOver only.
TEMPLATE_BOTTLE="${CITAVI_TEMPLATE_BOTTLE-Dot Net 48 Stock}"

# Draw Citavi at full Retina resolution (the same as CrossOver's "High Resolution Mode" switch;
# Wine has the same setting without a switch).
# Set to off for a display that is not Retina: with it on, everything there is twice as large.
# After changing it, run `citavi-crossover registry` and restart Citavi.
RETINA="${CITAVI_RETINA:-on}"

# Citavi's installer. `setup` and `update` use this file when no path is given, and download it
# from the link below (the one on Citavi's official download page) when it is missing.
CITAVI_MSI="${CITAVI_MSI:-$ROOT/resources/Citavi7Setup.msi}"
CITAVI_MSI_URL="${CITAVI_MSI_URL:-https://citavibackoffice.blob.core.windows.net/setup/Citavi7Setup.msi}"

# Where Citavi installs inside the bottle (Windows path and the same place seen from macOS).
CITAVI_WIN_DIR='C:\Program Files (x86)\Citavi 7'
CITAVI_UNIX_SUBDIR="drive_c/Program Files (x86)/Citavi 7"

# Where the login helper app is installed.
HELPER_INSTALL_DIR="${CITAVI_HELPER_DIR:-$HOME/Applications}"
