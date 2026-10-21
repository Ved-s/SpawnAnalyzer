set -e

function usage_and_exit() {
    echo "Usage:"
    echo "  ${0} vanillaWindows <terrariaPath>"
    echo "  ${0} vanillaNonWindows <terrariaPath>"
    echo "  ${0} tModLoaderNetcore <tMLSourceDir>"

    exit 1
}

function expect_file() {
    if [[ ! -f "${1}" ]]
    then
        echo "Expected file: ${1}"
        exit 2
    fi
}

#args: 1:srcRoot 2:dstRoot 3:path
function copy_ref() {
    echo "Copying reference: ${3}"
    dotnet refasmer -r -p --omit-non-api-members=true -o "${2}/${3}" "${1}/${3}"
}

flavor="${1}"
flavorPath="${2}"

if [[ -z "$flavor" || -z "$flavorPath" ]]
then
    usage_and_exit
fi

flavorPath=$(realpath "$flavorPath")
flavorDataDir="BuildTestData/$flavor"

case "$flavor" in
    "vanillaNonWindows")
        expect_file "$flavorPath/Terraria.exe";
        expect_file "$flavorPath/FNA.dll";

        rm -r "$flavorDataDir"
        mkdir "$flavorDataDir"

        copy_ref "$flavorPath" "$flavorDataDir" "Terraria.exe"
        copy_ref "$flavorPath" "$flavorDataDir" "FNA.dll"
        ;;

    "vanillaWindows")
        expect_file "$flavorPath/Terraria.exe";

        rm -r "$flavorDataDir"
        mkdir "$flavorDataDir"

        copy_ref "$flavorPath" "$flavorDataDir" "Terraria.exe"
        ;;

    "tModLoaderNetcore")
        binpath="$flavorPath/src/TerrariaNetCore/Terraria/bin/Release/net10.0"

        expect_file "$binpath/TerrariaRelease.dll";
        expect_file "$binpath/Libraries/FNA/1.0.0/FNA.dll";
        expect_file "$binpath/Libraries/ReLogic/1.0.0/ReLogic.dll";

        rm -r "$flavorDataDir"

        mkdir "$flavorDataDir"
        mkdir -p "$flavorDataDir/Libraries/FNA/1.0.0"
        mkdir -p "$flavorDataDir/Libraries/ReLogic/1.0.0"

        copy_ref "$binpath" "$flavorDataDir" "TerrariaRelease.dll"
        copy_ref "$binpath" "$flavorDataDir" "Libraries/FNA/1.0.0/FNA.dll"
        copy_ref "$binpath" "$flavorDataDir" "Libraries/ReLogic/1.0.0/ReLogic.dll"
        ;;

    *)
        echo "Unsupported terraria flavor $flavor"
        exit 1
        ;;
esac

echo "Done!"