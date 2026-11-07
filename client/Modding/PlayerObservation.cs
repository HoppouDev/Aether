using System.ComponentModel;

namespace Aether.Modding;

[Flags]
[EditorBrowsable(EditorBrowsableState.Never)]
public enum PlayerObservation
{
    None = 0,
    Character = 1,
    Health = 2,
    Mana = 4,
    Effects = 8
}
