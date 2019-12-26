ECHO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
ECHO Must be run from VisualStudio 2015 command prompt
ECHO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

REM https://docs.microsoft.com/en-us/nuget/schema/target-frameworks#supported-frameworks

mkdir ..\release
mkdir ..\release\net35
mkdir ..\release\net40
mkdir ..\release\net452
mkdir ..\release\net462
mkdir ..\release\netcoreapp1.1
mkdir ..\release\netcoreapp2.0
mkdir ..\release\netcoreapp2.2
mkdir ..\release\netstandard1.3
mkdir ..\release\netstandard1.4
mkdir ..\release\netstandard1.6 
REM mkdir ..\release\netstandard2.0
mkdir ..\release\Tools

del ..\release\net35\*.* /Q 
del ..\release\net40\*.* /Q
del ..\release\net452\*.* /Q
del ..\release\net462\*.* /Q
del ..\release\netcoreapp1.1\*.* /Q
del ..\release\netcoreapp2.0\*.* /Q
del ..\release\netcoreapp2.2\*.* /Q
del ..\release\netstandard1.3\*.* /Q
del ..\release\netstandard1.4\*.* /Q
del ..\release\netstandard1.6\*.* /Q

REM del ..\release\netstandard2.0\*.* /Q
del ..\release\Tools\*.* /Q

REM Build - Clean
msbuild.exe tik4net/tik4net.csproj /p:Configuration=Release /t:Clean
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:Configuration=Release /t:Clean
msbuild.exe Tools\tik4net.entitygenerator\tik4net.entitygenerator.csproj /p:Configuration=Release /t:Clean
msbuild.exe Tools\tik4net.entityWikiImporter\tik4net.entityWikiImporter.csproj /p:Configuration=Release /t:Clean
REM Build - Rebuild
msbuild.exe tik4net/tik4net.csproj /p:Configuration=Release /t:Rebuild
msbuild.exe tik4net.objects/tik4net.objects.csproj /p:Configuration=Release /t:Rebuild 
msbuild.exe Tools\tik4net.entitygenerator\tik4net.entitygenerator.csproj /p:Configuration=Release /t:Rebuild
msbuild.exe Tools\tik4net.entityWikiImporter\tik4net.entityWikiImporter.csproj /p:Configuration=Release /t:Rebuild

REM Copyt to release dir
copy tik4net\bin\Release\net35\tik4net.dll ..\release\net35\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.dll ..\release\net35\ /Y
copy tik4net\bin\Release\net40\tik4net.dll ..\release\net40\ /Y
copy tik4net.objects\bin\Release\net40\tik4net.objects.dll ..\release\net40\ /Y
copy tik4net\bin\Release\net452\tik4net.dll ..\release\net452\ /Y
copy tik4net.objects\bin\Release\net452\tik4net.objects.dll ..\release\net452\ /Y
copy tik4net\bin\Release\net462\tik4net.dll ..\release\net462\ /Y
copy tik4net.objects\bin\Release\net462\tik4net.objects.dll ..\release\net462\ /Y
copy tik4net\bin\Release\netcoreapp1.1\tik4net.dll ..\release\netcoreapp1.1\ /Y
copy tik4net.objects\bin\Release\netcoreapp1.1\tik4net.objects.dll ..\release\netcoreapp1.1\ /Y
copy tik4net\bin\Release\netcoreapp2.0\tik4net.dll ..\release\netcoreapp2.0\ /Y
copy tik4net.objects\bin\Release\netcoreapp2.0\tik4net.objects.dll ..\release\netcoreapp2.0\ /Y
copy tik4net\bin\Release\netcoreapp2.2\tik4net.dll ..\release\netcoreapp2.2\ /Y
copy tik4net.objects\bin\Release\netcoreapp2.2\tik4net.objects.dll ..\release\netcoreapp2.2\ /Y
copy tik4net\bin\Release\netstandard1.3\tik4net.dll ..\release\netstandard1.3\ /Y
copy tik4net.objects\bin\Release\netstandard1.3\tik4net.objects.dll ..\release\netstandard1.3\ /Y
copy tik4net\bin\Release\netstandard1.4\tik4net.dll ..\release\netstandard1.4\ /Y
copy tik4net.objects\bin\Release\netstandard1.4\tik4net.objects.dll ..\release\netstandard1.4\ /Y
copy tik4net\bin\Release\netstandard1.6\tik4net.dll ..\release\netstandard1.6\ /Y
copy tik4net.objects\bin\Release\netstandard1.6\tik4net.objects.dll ..\release\netstandard1.6\ /Y
REM documentation to release dir
copy tik4net\bin\Release\net35\tik4net.xml ..\release\net35\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.xml ..\release\net35\ /Y
copy tik4net\bin\Release\net35\tik4net.xml ..\release\net40\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.xml ..\release\net40\ /Y
copy tik4net\bin\Release\net35\tik4net.xml ..\release\net452\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.xml ..\release\net452\ /Y
copy tik4net\bin\Release\net35\tik4net.xml ..\release\net462\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.xml ..\release\net462\ /Y
copy tik4net\bin\Release\net35\tik4net.xml ..\release\netcoreapp1.1\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.xml ..\release\netcoreapp1.1\ /Y
copy tik4net\bin\Release\net35\tik4net.xml ..\release\netcoreapp2.0\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.xml ..\release\netcoreapp2.0\ /Y
copy tik4net\bin\Release\net35\tik4net.xml ..\release\netcoreapp2.2\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.xml ..\release\netcoreapp2.2\ /Y
copy tik4net\bin\Release\net35\tik4net.xml ..\release\netstandard1.3\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.xml ..\release\netstandard1.3\ /Y
copy tik4net\bin\Release\net35\tik4net.xml ..\release\netstandard1.4\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.xml ..\release\netstandard1.4\ /Y
copy tik4net\bin\Release\net35\tik4net.xml ..\release\netstandard1.6\ /Y
copy tik4net.objects\bin\Release\net35\tik4net.objects.xml ..\release\netstandard1.6\ /Y
REM copy tools
copy tik4net\bin\Release\net462\tik4net.dll ..\release\Tools\ /Y
copy tik4net.objects\bin\Release\net462\tik4net.objects.dll ..\release\Tools\ /Y
copy Tools\tik4net.entityWikiImporter\bin\Release\HtmlAgilityPack.dll ..\release\Tools\ /Y
copy Tools\tik4net.entityWikiImporter\bin\Release\tik4net.entityWikiImporter.exe.config   ..\release\Tools\ /Y
copy Tools\tik4net.entityWikiImporter\bin\Release\tik4net.entityWikiImporter.exe  ..\release\Tools\ /Y
copy Tools\tik4net.entityWikiImporter\bin\Release\info.txt ..\release\Tools\entityWikiImporter.info.txt /Y
copy Tools\tik4net.entitygenerator\bin\Release\tik4net.entitygenerator.exe.config   ..\release\Tools\ /Y
copy Tools\tik4net.entitygenerator\bin\Release\tik4net.entitygenerator.exe  ..\release\Tools\ /Y
copy Tools\tik4net.entitygenerator\bin\Release\info.txt ..\release\Tools\entitygenerator.info.txt /Y