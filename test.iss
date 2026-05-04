; Minimal test - does Inno Setup work at all?
[Setup]
AppName=Test
AppVersion=1.0
DefaultDirName={tmp}\test
OutputDir=artifacts

[Files]
Source: "LICENSE"; DestDir: "{app}"
