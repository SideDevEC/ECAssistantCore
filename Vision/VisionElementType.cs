namespace ECAssistant.Core.Vision;

/// <summary>
/// Semantic type of a detected UI element in a vision-structured analysis.
/// Serialized as lowercase strings; unknown values map to Other.
/// </summary>
public enum VisionElementType
{
    Header,
    Label,
    Button,
    Input,
    Checkbox,
    Radio,
    Select,
    Table,
    Image,
    Text,
    Other
}
