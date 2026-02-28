#!/usr/bin/bash

if [[ ! -d "BuildTestData" ]]
then
    echo "No TestBuildData directory, nothing to test"
    exit 0
fi

ec=0

for flavor in vanillaWindows vanillaNonWindows tModLoaderNetcore
do
    echo -e "\x1b[1mBuilding $flavor\x1b[0m"
    echo
    fullpath=$(realpath BuildTestData/$flavor)
    dotnet build -p ExtConfig=false -p "TerrariaPath=$fullpath" -p TerrariaFlavor=$flavor -p CopyOut=false -v d
    if [[ $? != 0 ]]
    then
        ec=1
    fi
    echo
done

exit $ec