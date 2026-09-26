#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
package_dir="$(mktemp -d)"
trap 'rm -rf "$package_dir"' EXIT

dotnet pack "$repo_root/FSharp.MinimalApi/FSharp.MinimalApi.fsproj" --configuration Release --output "$package_dir"
dotnet pack "$repo_root/FSharp.MinimalApi.OpenApi/FSharp.MinimalApi.OpenApi.fsproj" --configuration Release --output "$package_dir"

python3 - "$repo_root" "$package_dir" <<'PY'
from pathlib import Path
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

repo_root = Path(sys.argv[1])
package_dir = Path(sys.argv[2])
commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo_root, text=True).strip()
version = ET.parse(repo_root / "Directory.Build.props").findtext("./PropertyGroup/ArgylePackageVersion")
expected = {
    "ArgyleConcepts.FSharp.MinimalApi",
    "ArgyleConcepts.FSharp.MinimalApi.OpenApi",
}
packages = list(package_dir.glob("*.nupkg"))
assert len(packages) == 2, f"Expected two packages, found: {packages}"

for package in packages:
    with zipfile.ZipFile(package) as archive:
        files = set(archive.namelist())
        nuspec_name = next(name for name in files if name.endswith(".nuspec"))
        root = ET.fromstring(archive.read(nuspec_name))
        ns = {"n": "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"}
        metadata = root.find("n:metadata", ns)
        package_id = metadata.findtext("n:id", namespaces=ns)
        assert package_id in expected, package_id
        expected.remove(package_id)
        assert metadata.findtext("n:version", namespaces=ns) == version
        assert metadata.findtext("n:projectUrl", namespaces=ns) == "https://github.com/ArgyleConcepts/FSharp.MinimalApi"
        repository = metadata.find("n:repository", ns).attrib
        assert repository["url"] == "https://github.com/ArgyleConcepts/FSharp.MinimalApi"
        assert repository["type"] == "git"
        assert repository["commit"] == commit
        assert metadata.findtext("n:readme", namespaces=ns) == "README.md"
        assert metadata.findtext("n:license", namespaces=ns) == "MIT"
        assert {"LICENSE", "README.md"} <= files, files
        dependencies = {node.attrib["id"] for node in metadata.findall(".//n:dependency", ns)}
        assert "FSharp.Core" in dependencies, dependencies
        assert "FSharp.MinimalApi.Interop" not in dependencies, dependencies

        if package_id == "ArgyleConcepts.FSharp.MinimalApi":
            assert "lib/net10.0/FSharp.MinimalApi.dll" in files
            assert "lib/net10.0/FSharp.MinimalApi.Interop.dll" in files
            assemblies = ["FSharp.MinimalApi", "FSharp.MinimalApi.Interop"]
        else:
            assert "lib/net10.0/FSharp.MinimalApi.OpenApi.dll" in files
            assemblies = ["FSharp.MinimalApi.OpenApi"]

    symbols = package.with_suffix(".snupkg")
    with zipfile.ZipFile(symbols) as archive:
        for assembly in assemblies:
            assert f"lib/net10.0/{assembly}.pdb" in archive.namelist()
            source_link = repo_root / assembly / "obj/Release/net10.0" / f"{assembly}.sourcelink.json"
            documents = json.loads(source_link.read_text())["documents"]
            assert documents, source_link
            expected_url = f"https://raw.githubusercontent.com/ArgyleConcepts/FSharp.MinimalApi/{commit}/*"
            assert all(url == expected_url for url in documents.values()), documents

assert not expected, expected
print("Package contents, metadata, symbols and generated SourceLink mappings verified.")
PY

export NUGET_PACKAGES="$package_dir/cache"
export ArgylePackageVersion="$(dotnet msbuild "$repo_root/FSharp.MinimalApi/FSharp.MinimalApi.fsproj" -getProperty:ArgylePackageVersion)"
consumer_dir="$package_dir/consumer"
mkdir "$consumer_dir"
cp "$repo_root/eng/PackageSmoke/PackageSmoke.fsproj" "$repo_root/eng/PackageSmoke/Program.fs" "$consumer_dir/"
dotnet restore "$consumer_dir/PackageSmoke.fsproj" --source "$package_dir" --source https://api.nuget.org/v3/index.json
dotnet run --project "$consumer_dir/PackageSmoke.fsproj" --configuration Release --no-restore
