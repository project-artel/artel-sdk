# artel-sdk

Artel SDK is packaged for Unity through Unity Package Manager.

## Package

The Unity package lives at:

```text
Packages/kr.artel.sdk
```

Runtime scripts are under `Runtime/` and compiled through
`Artel.Runtime.asmdef`.

## Release builds

The SDK is a QA tool, so its runtime is kept out of release players. Two
assemblies split that responsibility:

| Assembly | Location | Compiled in |
| --- | --- | --- |
| `Artel.Attributes` | `Runtime/Attributes/` | always |
| `Artel.Runtime` | `Runtime/` | Editor and Development Build only |

`Artel.Runtime.asmdef` carries the define constraint
`UNITY_EDITOR || DEVELOPMENT_BUILD`, and `Runtime/Plugins/websocket-sharp.dll`
carries the same constraint through its plugin importer.

`Artel.Attributes` holds `[ArtelAction]` and `[ArtelState]` only. Game code that
tags its own `MonoBehaviour`s keeps compiling in release builds without any
conditional compilation of its own; the attributes stay as metadata and nothing
reads them. Action and input weaving is skipped in the same builds, because the
IL post-processor finds no `Artel.Runtime` to weave against.

### Verifying that a release build excludes the SDK

Build the player without *Development Build*, then list the managed assemblies
in the output:

```bash
ls <Build>_Data/Managed | grep -i artel
```

A release build lists `Artel.Attributes.dll` and nothing else from the SDK — no
`Artel.Runtime.dll` and no `websocket-sharp.dll`. A development build of the same
project lists all three. On macOS the same files live under
`<Build>.app/Contents/Resources/Data/Managed`.

## Sample

`samples/WordVenture` is included as the sample Unity project. It references
the local SDK package with:

```json
"kr.artel.sdk": "file:../../../Packages/kr.artel.sdk"
```

Open `samples/WordVenture` in Unity to try SDK runtime components from a real
Unity project.

## Tests and CI

Neither the repository root nor `samples/WordVenture` can run the package's
tests as checked out, so both local runs and CI assemble a throwaway Unity
project first:

```bash
.github/scripts/setup-unity-test-project.sh /tmp/artel-unity-test
```

`.github/workflows/unity-tests.yml` runs EditMode and PlayMode against that
project on every pull request and on every push to `develop`. It needs the Unity
licence secrets `UNITY_LICENSE` (or `UNITY_SERIAL` for Pro/Plus), `UNITY_EMAIL`,
and `UNITY_PASSWORD`; without them the workflow fails and names the missing one.

`.agents/docs/project.md` — *Running package tests* and *Continuous integration*
— has the full editor command line, where to obtain each secret, and how fork
pull requests are handled.
