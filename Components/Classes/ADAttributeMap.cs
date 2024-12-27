
using System.ComponentModel.DataAnnotations.Schema;

namespace nump.Components.Classes;

public class ADAttributeMap
{
    public string selectedAttribute {get; set;}
    public string? associatedColumn {get; set;}
    public bool required {get; set;}
    public bool allowUpdate {get; set;}
    public bool enabled {get; set;}
    [NotMapped]
    public Status Test = Status.Test;
    
}
public enum Status {
    Test
}