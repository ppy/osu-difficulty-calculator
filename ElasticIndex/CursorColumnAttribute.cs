using System;

namespace ElasticIndex
{
    /// <summary>
    /// Attributes which column to use as the cursor column.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class CursorColumnAttribute : Attribute
    {
        public string Name { get; private set; }
        public CursorColumnAttribute(string name) => Name = name;
    }
}
