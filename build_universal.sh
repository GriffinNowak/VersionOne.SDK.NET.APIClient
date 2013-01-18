
set -e

# This file must be updated by hand when you wish to bump the version number.
# The build script files will use these values in the build process

MAJOR=12
MINOR=3
REVISION=0

export NUGET_API_KEY="7df1856b-7448-4d94-bffd-6d8585fc1826"
export MYGET_API_KEY="05c7fd08-2673-411f-90fb-c794e632f32d"
export MYGET_REPO_URL="http://www.myget.org/F/versionone/api/v2/package"
export NUGET_FETCH_URL="$MYGET_REPO_URL;http://packages.nuget.org/api/v2/"

MAIN_DIR="APIClient"
MAIN_CSPROJ="VersionOne.SDK.APIClient.csproj"

TEST_DIR="APIClient.Tests"
TEST_CSPROJ="VersionOne.SDK.APIClient.Tests.csproj"
TEST_DLL="VersionOne.SDK.APIClient.Tests.dll"

NUNIT_RUNNER_NAME="nunit-console.exe"
NUNIT_XML_OUTPUT="nunit-objmodel-result.xml"

export Platform="AnyCPU"
export EnableNugetPackageRestore="true"
export Configuration="Release"

DOTNET_PATH="/c/Windows/Microsoft.Net/Framework/v4.0.30319"

# -------------------------------------------------------------------------




# ----- Utility functions 

function winpath() {
  # Convert gitbash style path '/c/Users/Big John/Development' to 'c:\Users\Big John\Development',
  # via dumb substitution.  handles drive letters; incurrs process creation penalty for sed
  echo "$1" | sed -e 's|^/\(\w\)/|\1:\\|g;s|/|\\|g'
}

function parentwith() {  # used to find $WORKSPACE, below.
  #Starting at the current dir and progressing up the ancestors,
  #retuns the first dir containing $1. If not found returns pwd.
  SEARCHTERM="$1"
  DIR=`pwd`
  while [ ! -e "$DIR/$SEARCHTERM" ]; do
    NEWDIR=`dirname "$DIR"`
    if [ "$NEWDIR" = "$DIR" ]; then
      pwd
      return
    fi
    DIR="$NEWDIR"
  done
  echo "$DIR"
  }


# If we aren't running under jenkins. some variables will be unset.
# So set them to a reasonable value

if [ -z "$WORKSPACE" ]; then
  export WORKSPACE=`parentwith .git`;
fi

for D in "$WORKSPACE/GetBuildTools" "$WORKSPACE/v1_build_tools" "$WORKSPACE/../v1_build_tools" .
do
  if [ -d $D ]; then
    export BUILDTOOLS_PATH="$D"
    echo "Chose $BUILDTOOLS_PATH for tools"
  fi
done

export PATH="$PATH:$BUILDTOOLS_PATH/bin:$DOTNET_PATH"

echo "PATH=$PATH"

if [ -z "$SIGNING_KEY_DIR" ]; then
  export SIGNING_KEY_DIR=`pwd`;
fi

export SIGNING_KEY="$SIGNING_KEY_DIR/VersionOne.snk"

if [ -f "$SIGNING_KEY" ]; then 
  export SIGN_ASSEMBLY="true"
else
  export SIGN_ASSEMBLY="false"
  echo "Please place VersionOne.snk in `pwd` or $SIGNING_KEY_DIR to enable signing.";
fi

if [ -z "$BUILD_NUMBER" ]; then
  # presume local workstation, set these to something
  export REVISION=`date +%y%j`  # last two digits of year + day of year
  export BUILD_NUMBER=`date +%H%M`  # hour + minute
fi


function update_nuget_deps() {
  PKGSCONFIG="${1:-packages.config}"
  if [ -f $PACKAGES_CONFIG ]
  then
    PKGSCONFIGW=`winpath "${PKGSCONFIG}"`
    PKGSDIRW=`winpath "$WORKSPACE/packages"`
    NuGet.exe install $PKGSCONFIGW -o $PKGSDIRW -Source $NUGET_FETCH_URL 
    NuGet.exe update $PKGSCONFIGW -Verbose -Source $NUGET_FETCH_URL
  fi
}



# ---- Produce .NET Metadata --------------------------------------------------------------

APICLIENT_PROPS_DIR="$WORKSPACE/$MAIN_DIR/Properties"
TESTS_PROPS_DIR="$WORKSPACE/$TEST_DIR/Properties"

mkdir -p "$APICLIENT_PROPS_DIR"
cat > "$APICLIENT_PROPS_DIR/AssemblyInfo.cs" <<EOF
// Auto generated by build_universal.sh at `date -u`

using System;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyVersion("$MAJOR.$MINOR.$REVISION.$BUILD_NUMBER")]
[assembly: AssemblyFileVersion("$MAJOR.$MINOR.$REVISION.$BUILD_NUMBER")]
[assembly: AssemblyInformationalVersion("See https://github.com/versionone/VersionOne.SDK.NET.APIClient/wiki")]

[assembly: AssemblyDescription("VersionOne SDK .NET API Client $Configuration Build")]
[assembly: AssemblyCompany("VersionOne, Inc.")]
[assembly: AssemblyProduct("VersionOne.SDK.APIClient")]
[assembly: AssemblyTitle("VersionOne SDK API Client")]
[assembly: AssemblyCopyright("Copyright `date +%Y`, VersionOne, Inc. Licensed under modified BSD.")]

[assembly: AssemblyConfiguration("$Configuration")]

EOF

mkdir -p "$TESTS_PROPS_DIR"
cat > "$TESTS_PROPS_DIR/AssemblyInfo.cs" <<EOF
// Auto generated by build_universal.sh at `date -u`

using System;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyVersion("$MAJOR.$MINOR.$REVISION.$BUILD_NUMBER")]
[assembly: AssemblyFileVersion("$MAJOR.$MINOR.$REVISION.$BUILD_NUMBER")]
[assembly: AssemblyInformationalVersion("12.2.1.3588 Summer 2012")]

[assembly: AssemblyDescription("VersionOne SDK .NET API Client Tests $Configuration Build")]
[assembly: AssemblyCompany("VersionOne, Inc.")]
[assembly: AssemblyProduct("VersionOne.SDK.APIClient.Tests")]
[assembly: AssemblyTitle("VersionOne SDK API Client Tests")]
[assembly: AssemblyCopyright("Copyright `date +%Y`, VersionOne, Inc. Licensed under modified BSD.")]

[assembly: AssemblyConfiguration("$Configuration")]

EOF



# ---- Build API Client using msbuild -----------------------------------------------------

cd $WORKSPACE/$MAIN_DIR
MSBuild.exe $MAIN_CSPROJ //p:SignAssembly=$SIGN_ASSEMBLY //p:AssemblyOriginatorKeyFile=`winpath "$SIGNING_KEY"`



# ---- Produce NuGet .nupkg file ----------------------------------------------------------
rm -rf *.nupkg
NuGet.exe pack $MAIN_CSPROJ -Symbols -prop Configuration=$Configuration



# ---- Build Tests ------------------------------------------------------------------------

cd $WORKSPACE/$TEST_DIR
update_nuget_deps  # this also gets the nunit runner used below
MSBuild.exe $TEST_CSPROJ //p:SignAssembly=$SIGN_ASSEMBLY //p:AssemblyOriginatorKeyFile=`winpath "$SIGNING_KEY"`



# ---- Run Tests --------------------------------------------------------------------------

cd $WORKSPACE
# Make sure the nunit-console is available first...
NUNIT_CONSOLE_RUNNER=`find packages | grep "${NUNIT_RUNNER_NAME}\$"`
if [ -z "$NUNIT_CONSOLE_RUNNER" ]
then
	echo "Could not find $NUNIT_RUNNER_NAME in the $WORKSPACE/packages folder."
	exit -1
fi

$NUNIT_CONSOLE_RUNNER \
  //framework:net-4.0 \
  //labels \
  //stoponerror \
  //xml=$NUNIT_XML_OUTPUT \
  `winpath "${WORKSPACE}/$TEST_DIR/bin/$Configuration/$TEST_DLL"`



