## Quick context

This is an s&box (Sandbox) game/addon project (C#). Runtime code lives in `Code/`, editor-only code lives in `Editor/`, UI Razor components are under `Code/ui/`, and scenes are under `Assets/scenes/`. Project-wide settings are in `ProjectSettings/`.

Keep in mind: this repo targets the s&box engine and references local s&box assemblies and the base addon library (see `Code/my_project.csproj` ProjectReference and the analyzer/reference entries). A working local s&box installation is required to build/run end-to-end.

## Big-picture architecture (what to know first)
- Core runtime components are small, component-style C# classes that inherit `Component` (e.g. `Code/PlayerMovement.cs`, `Code/CameraMovement.cs`). They use lifecycle methods: `OnAwake()`, `OnUpdate()`, and `OnFixedUpdate()`.
- UI is implemented with Razor components under `Code/ui/` (example: `Code/ui/HUD.razor`). Razor components include a `BuildHash()` override to control rebuilds.
- Editor extensions go into `Editor/` and use static helpers/attributes (example: `Editor/MyEditorMenu.cs` with `[Menu("Editor", "My Project/My Menu Option")]`).
- Input mappings are defined in `ProjectSettings/Input.config` and referenced directly by name (e.g. `Input.Down("Run")`, `Input.Pressed("Jump")` in `Code/PlayerMovement.cs`). Treat `Input.config` as the source of truth for action names.
- Scenes live in `Assets/scenes/` and the startup scene is set in `my_project.sbproj` metadata (`StartupScene: scenes/minimal.scene`).

## Project-specific conventions and patterns
- Use `[Property]` on component public properties to expose them to the editor/scene. Example: `CameraMovement` exposes `Player`, `Body`, `Head` as `[Property]`.
- Razor UI: prefer simple stateful components that expose a small `BuildHash()` to control rebuilds. Example: `HUD.razor` uses `BuildHash()` to include `MyStringValue`.
- Movement & camera logic: `PlayerMovement` manages inputs and character controller; `CameraMovement` uses the `CameraComponent` and scene tracing for third-person placement. When tracing or touching physics, prefer engine APIs (e.g., `Scene.Trace.Ray`, `Scene.PhysicsWorld`).
- Editor vs runtime: files under `Editor/` are compiled into the editor csproj; avoid referencing editor-only APIs from runtime `Code/` files.

## Integration points & external dependencies
- `Code/my_project.csproj` includes absolute references to s&box managed DLLs and a project reference to the base addon library (path: `E:\SteamLibrary\...\sbox\addons\base\Code\Base Library.csproj`). Developers must have s&box installed in the same machine or adjust paths.
- Output paths in `my_project.csproj` point to the local s&box output folder (see `<OutputPath>`), so builds publish into the engine install. Expect runtime testing to require the s&box client.

## Build / run / debug (discoverable commands)
- Open `my_project.slnx` in Visual Studio (or your editor) to load both `Code/` and `Editor/` projects.
- You can build the runtime project from PowerShell (project-root):

    dotnet build .\Code\my_project.csproj -c Debug

  Note: the project is set up to emit output into your local s&box folder (see `<OutputPath>`). If you don't have s&box installed in the same path, update the csproj references and OutputPath.
- For live testing: run the s&box client/editor; built outputs are copied into the s&box data folder and the engine will pick up the addon.

## Useful examples to reference when authoring code or PRs
- Input -> usage: `ProjectSettings/Input.config` defines `Run`, `Crouch`, `Jump`, `Forward`, `Attack1` etc. These exact strings are used by `Code/PlayerMovement.cs` via `Input.Down("Run")`, `Input.Pressed("Jump")`.
- Component pattern: see `Code/PlayerMovement.cs` and `Code/CameraMovement.cs` for lifecycle usage (`OnAwake`, `OnUpdate`, `OnFixedUpdate`) and how `GameObject` references (Head/Body) are used for transforms and animation helpers.
- UI component: `Code/ui/HUD.razor` demonstrates Razor usage inside the s&box project (note `[Property, TextArea]` and `BuildHash()` usage).

## What to watch for when editing
- Don't move runtime code into `Editor/` — editor code is compiled into a separate project and may reference editor-only APIs.
- Keep Input action names and groups consistent — changing an action name requires updating `Input.config` and all call sites.
- If you change project references or OutputPath in `Code/my_project.csproj`, document the change — builds depend on local s&box folder structure.

If anything above is unclear or you want more detail (examples, tests, or a short debug guide), tell me which area to expand and I will iterate.
