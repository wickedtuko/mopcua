# mopcua
Mono OPC UA 

To build from the command line
```shell
sudo apt install nuget
cd ~/<solution_dir>
nuget restore MonoOPC.sln
msbuild
```

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
