# mopcua
Mono OPC UA 

To build from the command line for Mono
```shell
sudo apt install nuget
cd ~/<solution_dir>
nuget restore MonoOPC.sln
msbuild  opcuac/opcuac.csproj
```

To build from the command line for dotnet  
```shell
cd ~/<solution_dir>/opcuac
dotnet build dotopcuac.csproj
```

Tested on:  
- Ubuntu 20.10, Debian 10, Pop!\_OS 20.10, Mono 6.12.0.107  
- Windows 10 1909/20H2, VS 2019, .Net 4.7.2

## Mono OPC UA Client
Console Mono OPC UA client - subscribe to items
* Pass nodeID 
```shell
mono opcuac.exe --url=opc.tcp://localhost:51210/UA/SampleServer --nodeID="i=2258" -a -t 3
```
* Pass a file with node ids
```shell
mono opcuac.exe --url=opc.tcp://localhost:51210/UA/SampleServer -a -t 3 --NodeFile=nodeid.txt
```
