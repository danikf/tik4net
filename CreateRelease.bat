ECHO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
ECHO Must be run from VisualStudio 2015 command prompt
ECHO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

mkdir ..\release
mkdir ..\release\NET-3.5
mkdir ..\release\NET-4.0

REM .NET 3.5
msbuild.exe tik4net/tik4net.csproj /p:DefineConstants="V35";TargetFrameworkVersion=v3.5;Configuration=Release /tv:4.0 /t:Clean
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:DefineConstants="V35";TargetFrameworkVersion=v3.5;Configuration=Release /tv:4.0 /t:Clean
msbuild.exe tik4net/tik4net.csproj /p:DefineConstants="V35";TargetFrameworkVersion=v3.5;Configuration=Release /tv:4.0 /t:Rebuild
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:DefineConstants="V35";TargetFrameworkVersion=v3.5;Configuration=Release /tv:4.0 /t:Rebuild
xcopy tik4net\bin\Release\tik4net.dll ..\release\NET-3.5\ /Y
xcopy tik4net.objects\bin\Release\tik4net.objects.dll ..\release\NET-3.5\ /Y

REM .NET 4.0
msbuild.exe tik4net/tik4net.csproj /p:DefineConstants="V40";TargetFrameworkVersion=v4.0;Configuration=Release /tv:4.0 /t:Clean
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:DefineConstants="V40";TargetFrameworkVersion=v4.0;Configuration=Release /tv:4.0 /t:Clean
msbuild.exe tik4net/tik4net.csproj /p:DefineConstants="V40";TargetFrameworkVersion=v4.0;Configuration=Release /tv:4.0 /t:Rebuild
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:DefineConstants="V40";TargetFrameworkVersion=v4.0;Configuration=Release /tv:4.0 /t:Rebuild
xcopy tik4net\bin\Release\tik4net.dll ..\release\NET-4.0\ /Y
xcopy tik4net.objects\bin\Release\tik4net.objects.dll ..\release\NET-4.0\ /Y

REM .NET 4.5.2 (or defined in *.csproj)
msbuild.exe tik4net/tik4net.csproj /p:Configuration=Release /t:Clean
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:Configuration=Release /tv:4.0 /t:Clean
msbuild.exe tik4net/tik4net.csproj /p:Configuration=Release /t:Rebuild
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:Configuration=Release /tv:4.0 /t:Rebuild
xcopy tik4net\bin\Release\tik4net.dll ..\release\ /Y
xcopy tik4net.objects\bin\Release\tik4net.objects.dll ..\release\ /Y