# Python Launcher
This project provides a straightforward C# executable designed to simplify running Python scripts for end-users. It automatically handles the Python environment setup and dependency installation, abstracting away complexities like pip and virtual environments. The main goal launch scripts without installing python into system.

## Features
- **Automatic Python Setup**: Downloads and configures the latest stable Python (Windows x64 embeddable distribution) if not already present.

- **Self-Contained**: Creates an isolated EmbeddedPython environment within the application's directory.

- **Dependency Management**: Automatically installs Python package dependencies declared in your scripts using pip.

- **User-Friendly Installer**: Includes an Inno Setup script for easy, wizard-based installation.

- **Isolated Execution**: Runs Python scripts in separate console windows for clear output and interaction.

# Getting started

## Usage
1. Install the Application:

    - Download PythonLauncher-Setup.exe.

    - Run the installer, which places the application files (including PythonLauncher.exe, install_deps.py and some other) into desired folder.

2. Run Your Python Script:

    - Launch the installed PythonLauncher.exe via its Start Menu or desktop shortcut.

    - On first run, it automatically downloads and configures the latest Python environment.

    - When prompted, enter the full path or filename of your Python script (e.g., my_script.py if it's in the application's installed folder, or C:\Path\To\Your\script.py).

    - Dependency installation and your script will run in separate console windows.

## Troubleshooting
- **"Access to the path 'C:\Program Files...' is denied."**: The application was installed to a protected system directory. Solution: Run as administrator.

- **"Error: Could not determine the latest Python embed download URL."**: Check internet connection. The Python FTP site structure or regex might need updating in program.cs.

## License
This project is open-source, released under the [MIT License](LICENSE).
