#!/bin/bash
SCRIPTPATH="$( cd "$(dirname "$0")" >/dev/null 2>&1 ; pwd -P )"
OPC_EXE="opcuac.exe"
EXE="$(dirname "$SCRIPTPATH")"/$OPC_EXE
mono $EXE -a -t 3 --url=opc.tcp://localhost:51210/UA/SampleServer