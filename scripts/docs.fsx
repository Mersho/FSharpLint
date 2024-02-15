#load "common.fsx"
#load "buildrelease.fsx"

open Common

exec "dotnet"  @"fornax build" "docs"
