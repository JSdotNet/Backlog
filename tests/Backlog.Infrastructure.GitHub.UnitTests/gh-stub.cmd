@echo off
rem ---------------------------------------------------------------------------
rem  A stand-in for the `gh` CLI, driven entirely by the files beside it.
rem
rem  GhCliTransport launches a process, so the only seam it offers is the
rem  executable it launches. Everything a test can say to it therefore has to
rem  arrive as a file next to this script, and everything a test can observe has
rem  to leave as argv, stdout, stderr or an exit code.
rem
rem  Reads:   stdout.txt     written to stdout verbatim, when it exists
rem           stderr.txt     written to stderr verbatim, when it exists
rem           exit-code.txt  the exit code to end with; 0 when absent
rem  Writes:  args.txt       one argv element per line, each call closed by the
rem                          marker line below
rem           stdin.txt      whatever was piped in, but only for a call that
rem                          said `--input` -- draining a stdin the caller never
rem                          redirected would block on the inherited handle
rem
rem  A redirection is written before its command throughout, because a line
rem  ending in a digit followed by `>>` reads as a file-handle redirect and one
rem  of the arguments this records ends in `2022-11-28`.
rem ---------------------------------------------------------------------------
setlocal

set "here=%~dp0"

:record
if "%~1"=="" goto recorded
>>"%here%args.txt" echo(%~1
if /i "%~1"=="--input" set "piped=yes"
shift
goto record

:recorded
>>"%here%args.txt" echo(--- end of call ---

if defined piped more >>"%here%stdin.txt"

if exist "%here%stdout.txt" type "%here%stdout.txt"
if exist "%here%stderr.txt" >&2 type "%here%stderr.txt"

set "code=0"
if exist "%here%exit-code.txt" set /p code=<"%here%exit-code.txt"
exit /b %code%
