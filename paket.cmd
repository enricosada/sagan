@echo off
pushd %~dp0
call .paket\paket.exe %*
popd
