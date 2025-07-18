#!/bin/bash

set -e  # Exit immediately if a command exits with a non-zero status

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

CONFIG_DIR="$HOME/.config"
ORIGINAL_DIR="$CONFIG_DIR/VintagestoryData"
MAIN_DIR="$CONFIG_DIR/VintagestoryData-main"
DEBUG_DIR="$CONFIG_DIR/VintagestoryData-debug"
RELEASES_DIR="$SCRIPT_DIR/Releases"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
NC='\033[0m' # No Color

print_help() {
    echo "Vintagestory Profile Manager"
    echo ""
    echo "Usage: $0 [command]"
    echo ""
    echo "Commands:"
    echo "  main    - Switch to main profile"
    echo "  debug   - Switch to debug profile (with latest mod symlinks)"
    echo "  status  - Show current profile"
    echo "  help    - Show this help message"
    echo ""
}

# Find latest release of a mod
find_latest_mod() {
    local pattern="$1"
    local latest_file=""
    local latest_version=""
    
    # Check if releases directory exists
    if [ ! -d "$RELEASES_DIR" ]; then
        echo -e "${RED}Error: Releases directory not found: $RELEASES_DIR${NC}"
        return 1
    fi
    
    # Find files matching the pattern
    for file in "$RELEASES_DIR"/${pattern}_*.zip; do
        # Skip if no matches found
        [ -e "$file" ] || continue
        
        # Extract version from filename
        local filename=$(basename "$file")
        local version=${filename#${pattern}_}
        version=${version%.zip}
        
        # Compare versions (simple string comparison for now, can be improved)
        if [[ "$version" > "$latest_version" ]]; then
            latest_version="$version"
            latest_file="$file"
        fi
    done
    
    if [ -z "$latest_file" ]; then
        echo ""
    else
        echo "$latest_file"
    fi
}

# Setup mod symlinks for debug profile
setup_debug_mod_symlinks() {
    local debug_mods_dir="$DEBUG_DIR/Mods"
    
    # Create mods directory if it doesn't exist
    mkdir -p "$debug_mods_dir"
    
    echo -e "${YELLOW}Setting up mod symlinks for debug profile...${NC}"
    
    # Find latest versions of mods
    local vs_mod=$(find_latest_mod "vintagesymphony")
    local vs_assets=$(find_latest_mod "vintagesymphonyassets")
    
    # Check if mods were found
    if [ -z "$vs_mod" ]; then
        echo -e "${RED}Error: Could not find VintageSymphony mod in Releases directory${NC}"
        return 1
    fi
    
    if [ -z "$vs_assets" ]; then
        echo -e "${RED}Error: Could not find VintageSymphony assets mod in Releases directory${NC}"
        return 1
    fi
    
    # Get base filenames
    local vs_mod_filename=$(basename "$vs_mod")
    local vs_assets_filename=$(basename "$vs_assets")
    
    # Create symlinks for VintageSymphony mods
    setup_mod_symlink "$vs_mod" "$debug_mods_dir/$vs_mod_filename"
    setup_mod_symlink "$vs_assets" "$debug_mods_dir/$vs_assets_filename"
    
    echo -e "${GREEN}Successfully set up mod symlinks for debug profile.${NC}"
}

# Helper function to create a mod symlink
setup_mod_symlink() {
    local source="$1"
    local target="$2"
    
    # Check if target already exists
    if [ -L "$target" ]; then
        # It's already a symlink, update it if it's not pointing to the source
        if [ "$(readlink "$target")" != "$source" ]; then
            rm "$target"
            ln -s "$source" "$target"
            echo -e "Updated symlink: ${GREEN}$target${NC} -> ${GREEN}$source${NC}"
        else
            echo -e "Symlink already exists: ${GREEN}$target${NC} -> ${GREEN}$source${NC}"
        fi
    elif [ -e "$target" ]; then
        # It exists but is not a symlink, replace it
        echo -e "${YELLOW}Replacing file with symlink: $target${NC}"
        rm "$target"
        ln -s "$source" "$target"
        echo -e "Created symlink: ${GREEN}$target${NC} -> ${GREEN}$source${NC}"
    else
        # Does not exist, create new symlink
        ln -s "$source" "$target"
        echo -e "Created symlink: ${GREEN}$target${NC} -> ${GREEN}$source${NC}"
    fi
}

setup_directories() {
    # Check if original directory exists and is not a symlink
    if [ -d "$ORIGINAL_DIR" ] && [ ! -L "$ORIGINAL_DIR" ]; then
        echo -e "${YELLOW}Original Vintagestory directory exists.${NC}"
        
        # Check if main directory already exists
        if [ -d "$MAIN_DIR" ]; then
            echo -e "${RED}Both original and main directories exist.${NC}"
            echo "Please manually resolve this conflict:"
            echo "1. Move or merge content from $ORIGINAL_DIR to $MAIN_DIR"
            echo "2. Then run this script again"
            exit 1
        else
            echo -e "${YELLOW}Moving original directory to main profile...${NC}"
            mv "$ORIGINAL_DIR" "$MAIN_DIR"
            echo -e "${GREEN}Successfully moved original directory to main profile.${NC}"
        fi
    fi

    # Create main directory if it doesn't exist
    if [ ! -d "$MAIN_DIR" ]; then
        echo -e "${YELLOW}Creating main profile directory...${NC}"
        mkdir -p "$MAIN_DIR"
        echo -e "${GREEN}Created main profile directory.${NC}"
    fi

    # Create debug directory if it doesn't exist
    if [ ! -d "$DEBUG_DIR" ]; then
        echo -e "${YELLOW}Creating debug profile directory...${NC}"
        mkdir -p "$DEBUG_DIR"
        echo -e "${GREEN}Created debug profile directory.${NC}"
    fi
}

switch_profile() {
    local target_dir="$1"
    local profile_name="$2"
    
    # Remove existing symlink if it exists
    if [ -L "$ORIGINAL_DIR" ]; then
        rm "$ORIGINAL_DIR"
    elif [ -d "$ORIGINAL_DIR" ] && [ ! -L "$ORIGINAL_DIR" ]; then
        echo -e "${RED}Error: $ORIGINAL_DIR exists and is not a symlink.${NC}"
        echo "Please run this script again to properly set up the directories."
        exit 1
    fi
    
    # Create symlink
    ln -s "$target_dir" "$ORIGINAL_DIR"
    echo -e "${GREEN}Switched to $profile_name profile.${NC}"
    
    # Additional setup for debug profile
    if [ "$profile_name" = "debug" ]; then
        setup_debug_mod_symlinks
    fi
}

get_current_profile() {
    if [ -L "$ORIGINAL_DIR" ]; then
        target=$(readlink "$ORIGINAL_DIR")
        if [ "$target" = "$MAIN_DIR" ]; then
            echo -e "Current profile: ${GREEN}main${NC}"
        elif [ "$target" = "$DEBUG_DIR" ]; then
            echo -e "Current profile: ${GREEN}debug${NC}"
        else
            echo -e "Current profile: ${YELLOW}unknown${NC} (linked to $target)"
        fi
    elif [ -d "$ORIGINAL_DIR" ] && [ ! -L "$ORIGINAL_DIR" ]; then
        echo -e "Current profile: ${YELLOW}original${NC} (not managed by this script)"
    else
        echo -e "Current profile: ${RED}none${NC} (Vintagestory directory doesn't exist)"
    fi
}

# Main script logic
setup_directories

case "$1" in
    main)
        switch_profile "$MAIN_DIR" "main"
        ;;
    debug)
        switch_profile "$DEBUG_DIR" "debug"
        ;;
    status)
        get_current_profile
        ;;
    help|--help|-h|"")
        print_help
        ;;
    *)
        echo -e "${RED}Unknown command: $1${NC}"
        print_help
        exit 1
        ;;
esac
