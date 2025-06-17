# install_deps.py
# -*- coding: utf-8 -*-
import subprocess
import sys
import os
import re
import importlib.util
import builtins

# Define paths relative to the current script's location
EMBEDDED_PYTHON_DIR = os.path.dirname(sys.executable)

def is_installed(module_name):
    """
    Checks if a Python module is already installed or is a built-in module.
    """
    if module_name in sys.builtin_module_names or module_name in builtins.__dict__:
        return True  # Built-in module
    return importlib.util.find_spec(module_name) is not None

def ensure_pip():
    """
    Ensures pip is installed in the embedded Python environment.
    If pip is not found, it attempts to install it using ensurepip.
    """
    try:
        importlib.util.find_spec("pip")
        print("pip is already installed.")
    except ImportError:
        print("pip not found. Attempting to install pip using ensurepip...")
        try:
            result = subprocess.run([sys.executable, "-m", "ensurepip", "--default-pip"],
                                    capture_output=True, text=True, check=True)
            print("ensurepip Output:\n", result.stdout)
            if result.stderr:
                print("ensurepip Errors:\n", result.stderr)
            print("pip installed successfully.")
        except subprocess.CalledProcessError as e:
            print(f"Error installing pip: {e.stderr}")
            print("Please ensure your embedded Python distribution is complete and supports ensurepip.")
            sys.exit(1)
        except Exception as e:
            print(f"An unexpected error occurred while ensuring pip: {e}")
            sys.exit(1)

def install_package_with_pip(package_name):
    """
    Installs a Python package using pip.
    This function will simply try to install the package.
    If it fails (e.g., package not found on PyPI, or compilation issues),
    it will report the error but NOT exit the script.
    """
    print(f"Attempting to install '{package_name}' using pip...")
    try:
        command_args = [sys.executable, "-m", "pip", "install", package_name]

        # Use check=False so that subprocess.run doesn't raise an exception
        # on non-zero exit codes. We'll handle the return code manually.
        result = subprocess.run(command_args,
                                capture_output=True, text=True, check=False) # <--- IMPORTANT CHANGE: check=False

        if result.returncode == 0:
            print(f"Successfully installed '{package_name}'.")
            if result.stdout:
                print("Pip Output:\n", result.stdout)
            if result.stderr:
                print("Pip Warnings/Errors:\n", result.stderr) # Print stderr even on success, for warnings
        else:
            print(f"Failed to install '{package_name}' using pip.")
            print(f"  Command: {' '.join(command_args)}")
            print(f"  Return Code: {result.returncode}")
            print(f"  Error Output:\n{result.stderr}")
            print(f"Skipping '{package_name}' as it could not be installed.")
    except Exception as e:
        print(f"An unexpected error occurred while attempting to install '{package_name}': {e}")
        print(f"Skipping '{package_name}' due to unexpected error.")


def extract_imports(script_path):
    """
    Extracts top-level module names from import statements in a Python script.
    It will only exclude built-in Python modules.
    """
    modules = set()
    try:
        with open(script_path, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                # Skip comments and empty lines
                if not line or line.startswith('#'):
                    continue

                # Regex for 'import module_name' or 'import module_name as alias'
                match = re.match(r"^\s*import\s+([a-zA-Z_][a-zA-Z0-9_.]*)", line)
                if match:
                    module_name = match.group(1).split(".")[0]
                    # Only add if it's not a built-in module
                    if not is_installed(module_name):
                        modules.add(module_name)
                    continue

                # Regex for 'from module_name import ...' or 'from package.submodule import ...'
                match = re.match(r"^\s*from\s+([a-zA-Z_][a-zA-Z0-9_.]*)\s+import", line)
                if match:
                    module_name = match.group(1).split(".")[0]
                    # Only add if it's not a built-in module
                    if not is_installed(module_name):
                        modules.add(module_name)
                    continue
    except FileNotFoundError:
        print(f"Error: Script file not found at '{script_path}' for import extraction.")
    except Exception as e:
        print(f"An error occurred during import extraction from '{script_path}': {e}")
    return modules


def install_missing_dependencies(script_path):
    """
    Identifies missing dependencies for a given script and attempts to install them.
    Installation failures for individual packages are reported but do not stop the process.
    """
    if not os.path.exists(script_path):
        print(f"Error: Python script '{script_path}' not found. Cannot resolve dependencies.")
        return

    print(f"Analyzing '{script_path}' for dependencies...")
    required_modules = extract_imports(script_path)
    print(f"Identified potential dependencies: {', '.join(required_modules) if required_modules else 'None'}")

    for module in required_modules:
        if not is_installed(module):
            print(f"'{module}' is not installed. Attempting installation...")
            install_package_with_pip(module)
        else:
            print(f"'{module}' is already installed.")

if __name__ == "__main__":
    ensure_pip()

    if len(sys.argv) < 2:
        print("Error: No target Python script filename provided to install_deps.py.")
        print("Usage: python install_deps.py <path_to_main_script.py>")
        sys.exit(1)

    main_script_path = sys.argv[1]
    install_missing_dependencies(main_script_path)