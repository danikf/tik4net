ECHO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
ECHO Must be run from VisualStudio 2015 command prompt
ECHO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

mkdir ..\release
mkdir ..\release\NET35
mkdir ..\release\NET40
mkdir ..\release\NET45
mkdir ..\release\NET46
mkdir ..\release\Tools

REM .NET 3.5
msbuild.exe tik4net/tik4net.csproj /p:DefineConstants="V35";TargetFrameworkVersion=v3.5;Configuration=Release /tv:4.0 /t:Clean
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:DefineConstants="V35";TargetFrameworkVersion=v3.5;Configuration=Release /tv:4.0 /t:Clean
msbuild.exe tik4net/tik4net.csproj /p:DefineConstants="V35";TargetFrameworkVersion=v3.5;Configuration=Release /tv:4.0 /t:Rebuild
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:DefineConstants="V35";TargetFrameworkVersion=v3.5;Configuration=Release /tv:4.0 /t:Rebuild
copy tik4net\bin\Release\tik4net.dll ..\release\NET35\ /Y
copy tik4net.objects\bin\Release\tik4net.objects.dll ..\release\NET35\ /Y

REM .NET 4.0
msbuild.exe tik4net/tik4net.csproj /p:DefineConstants="V40";TargetFrameworkVersion=v4.0;Configuration=Release /tv:4.0 /t:Clean
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:DefineConstants="V40";TargetFrameworkVersion=v4.0;Configuration=Release /tv:4.0 /t:Clean
msbuild.exe tik4net/tik4net.csproj /p:DefineConstants="V40";TargetFrameworkVersion=v4.0;Configuration=Release /tv:4.0 /t:Rebuild
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:DefineConstants="V40";TargetFrameworkVersion=v4.0;Configuration=Release /tv:4.0 /t:Rebuild
copy tik4net\bin\Release\tik4net.dll ..\release\NET40\ /Y
copy tik4net.objects\bin\Release\tik4net.objects.dll ..\release\NET40\ /Y

REM .NET 4.5.2
msbuild.exe tik4net/tik4net.csproj /p:TargetFrameworkVersion=v4.5.2;Configuration=Release /t:Clean
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:TargetFrameworkVersion=v4.5.2;Configuration=Release /t:Clean
msbuild.exe tik4net/tik4net.csproj /p:TargetFrameworkVersion=v4.5.2;Configuration=Release /t:Rebuild
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:TargetFrameworkVersion=v4.5.2;Configuration=Release /t:Rebuild
copy tik4net\bin\Release\tik4net.dll ..\release\NET45\ /Y
copy tik4net.objects\bin\Release\tik4net.objects.dll ..\release\NET45\ /Y

REM .NET 4.6.1 (or defined in *.csproj)
msbuild.exe tik4net/tik4net.csproj /p:Configuration=Release /t:Clean
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:Configuration=Release /t:Clean
msbuild.exe tik4net/tik4net.csproj /p:Configuration=Release /t:Rebuild
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:Configuration=Release /t:Rebuild
copy tik4net\bin\Release\tik4net.dll ..\release\NET46\ /Y
copy tik4net.objects\bin\Release\tik4net.objects.dll ..\release\NET46\ /Y

REM tools
msbuild.exe Tools\tik4net.entitygenerator\tik4net.entitygenerator.csproj /p:Configuration=Release /t:Clean
msbuild.exe Tools\tik4net.entitygenerator\tik4net.entitygenerator.csproj /p:Configuration=Release /t:Rebuild
msbuild.exe Tools\tik4net.entityWikiImporter\tik4net.entityWikiImporter.csproj /p:Configuration=Release /t:Clean
msbuild.exe Tools\tik4net.entityWikiImporter\tik4net.entityWikiImporter.csproj /p:Configuration=Release /t:Rebuild

copy tik4net\bin\Release\tik4net.dll ..\release\Tools\ /Y
copy tik4net.objects\bin\Release\tik4net.objects.dll ..\release\Tools\ /Y
copy Tools\tik4net.entityWikiImporter\bin\Release\HtmlAgilityPack.dll ..\release\Tools\ /Y
copy Tools\tik4net.entityWikiImporter\bin\Release\tik4net.entityWikiImporter.exe.config   ..\release\Tools\ /Y
copy Tools\tik4net.entityWikiImporter\bin\Release\tik4net.entityWikiImporter.exe  ..\release\Tools\ /Y
copy Tools\tik4net.entityWikiImporter\bin\Release\info.txt ..\release\Tools\entityWikiImporter.info.txt /Y
copy Tools\tik4net.entitygenerator\bin\Release\tik4net.entitygenerator.exe.config   ..\release\Tools\ /Y
copy Tools\tik4net.entitygenerator\bin\Release\tik4net.entitygenerator.exe  ..\release\Tools\ /Y
copy Tools\tik4net.entitygenerator\bin\Release\info.txt ..\release\Tools\entitygenerator.info.txt /Y
