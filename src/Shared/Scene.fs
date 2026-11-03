namespace Shared

/// Represents a position in millimetres or rotation in degrees in a local frame.
type SceneVector = { X: float; Y: float; Z: float }

/// Represents the supported engine-independent scene solids.
type SceneShape =
    | Group
    | Box of halfLengths: SceneVector
    | Cylinder of
        innerRadius: float *
        outerRadius: float *
        halfLength: float *
        startDegrees: float *
        sweepDegrees: float
    | Cad of path: string * unitMm: float
    | Unsupported of componentType: string

/// Represents a component with an explicit parent frame and local placement.
type SceneComponent = {
    Name: string
    Parent: string option
    Shape: SceneShape
    Translation: SceneVector
    Rotation: SceneVector
    Material: string option
    Color: string option
    Included: bool
    Invisible: bool
    Parallel: bool
}

/// Represents a scorer attached to a volume or named local surface.
type SceneScorer = {
    Name: string
    Component: string
    Surface: string option
    Quantity: string
    Bins: (string * int) list
    Active: bool
}

/// Represents a particle source attached to a geometry frame.
type SceneSource = {
    Name: string
    Component: string
    SourceType: string
}

/// Represents an inspected simulation without any renderer-specific objects.
type SimulationScene = {
    Components: SceneComponent list
    Scorers: SceneScorer list
    Sources: SceneSource list
    Warnings: string list
}

/// Identifies one generated input relative to the configured inputs directory.
type SceneInput = {
    Id: string
    Batch: string
    FileName: string
}

/// Requests a CAD asset only through its owning generated input and component.
type SceneAssetRequest = { InputId: string; Component: string }

/// Carries a bounded STL asset without exposing a general file-reading endpoint.
type SceneAsset = { Name: string; Base64: string }