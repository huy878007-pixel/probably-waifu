@echo off
chcp 65001 >nul
python "%~dp0..\tools\build_workbench_runtime.py"
if errorlevel 1 (
  echo 更新失败，请检查 Python 和上方错误。
) else (
  echo 快照已更新，请手动刷新工作台网页。
)
pause
