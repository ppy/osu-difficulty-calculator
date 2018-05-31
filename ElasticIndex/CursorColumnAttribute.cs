// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

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
