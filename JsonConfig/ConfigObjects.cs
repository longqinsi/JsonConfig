//
// Copyright (C) 2012 Timo Dörr
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using JsonFx.Json;

namespace JsonConfig
{
    public class ConfigObject : DynamicObject, IDictionary<string, object>
    {
        private class ConfigObjectMember
        {
            public bool IsDefault
            {
                get { return _isDefault; }
            }

            public object Value
            {
                get { return _value; }
            }

            private readonly bool _isDefault;
            private readonly object _value;

            public ConfigObjectMember(bool isDefault, object value)
            {
                _isDefault = isDefault;
                _value = value;
            }
        }

        private readonly bool _isDefault;
        private volatile ConcurrentDictionary<string, ConfigObjectMember> _members = new ConcurrentDictionary<string, ConfigObjectMember>();

        #region IEnumerable implementation

        public IEnumerator GetEnumerator()
        {
            return _members.GetEnumerator();
        }

        #endregion

        #region IEnumerable implementation

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
        {
            return _members.ToList().Select(a => new KeyValuePair<string, object>(a.Key, a.Value.Value)).GetEnumerator();
        }

        #endregion

        public static ConfigObject FromExpando(ExpandoObject e, bool isDefault = false)
        {
            var edict = e as IDictionary<string, object>;
            var c = new ConfigObject(isDefault);
            var cdict = c._members;

            // this is not complete. It will, however work for JsonFX ExpandoObjects
            // which consits only of primitive types, ExpandoObject or ExpandoObject [] 
            // but won't work for generic ExpandoObjects which might include collections etc.
            foreach (var kvp in edict)
            {
                // recursively convert and add ExpandoObjects
                if (kvp.Value is ExpandoObject)
                {
                    cdict.TryAdd(kvp.Key, new ConfigObjectMember(isDefault, FromExpando((ExpandoObject)kvp.Value, isDefault)));
                }
                else if (kvp.Value is ExpandoObject[])
                {
                    var configObjects = new List<ConfigObject>();
                    foreach (var ex in ((ExpandoObject[])kvp.Value))
                    {
                        configObjects.Add(FromExpando(ex, isDefault));
                    }
                    cdict.TryAdd(kvp.Key, new ConfigObjectMember(isDefault, configObjects.ToArray()));
                }
                else
                {
                    cdict.TryAdd(kvp.Key, new ConfigObjectMember(isDefault, kvp.Value));
                }
            }
            return c;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            ConfigObjectMember member;
            if (_members.TryGetValue(binder.Name, out member))
            {
                result = member.Value;
            }
            else
            {
                result = new ConfigObject(IsDefault);
                _members[binder.Name] = new ConfigObjectMember(IsDefault, result);
            }
            return true;
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            _members[binder.Name] = new ConfigObjectMember(_isDefault, value);
            return true;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            // some special methods that should be in our dynamic object
            if (binder.Name == "ApplyJsonFromFile" && args.Length == 1 && args[0] is string)
            {
                result = Config.ApplyJsonFromFileInfo(new FileInfo((string)args[0]), this);
                return true;
            }
            if (binder.Name == "ApplyJsonFromFile" && args.Length == 1 && args[0] is FileInfo)
            {
                result = Config.ApplyJsonFromFileInfo((FileInfo)args[0], this);
                return true;
            }
            if (binder.Name == "Clone")
            {
                result = Clone();
                return true;
            }
            if (binder.Name == "Exists" && args.Length == 1 && args[0] is string)
            {
                result = _members.ContainsKey((string)args[0]);
                return true;
            }

            // no other methods availabe, error
            result = null;
            return false;
        }

        public override string ToString()
        {
            if (_members.Count == 0)
            {
                return "";
            }
            var w = new JsonWriter();
            //return w.Write(_members);
            return w.Write(GetMapForOutput(true));
        }

        private IDictionary<string, object> GetMapForOutput(bool isOutputDefault)
        {
            return _members.Where(a => (isOutputDefault || !a.Value.IsDefault))
                .Select(a => new KeyValuePair<string, object>(a.Key,
                    (a.Value.Value is ConfigObject)
                        ? ((ConfigObject) a.Value.Value).GetMapForOutput(isOutputDefault)
                        : a.Value.Value))
                .Where(
                    a => !(a.Value is IDictionary<string, object>) || ((IDictionary<string, object>) a.Value).Count > 0)
                .ToDictionary(a => a.Key, a => a.Value);
        }

        /// <summary>
        ///     Get the json string to save to user config json file. The config items from default config will not be saved.
        /// </summary>
        /// <returns></returns>
        internal string GetJsonToSave()
        {
            var w = new JsonWriter();
            w.Settings.PrettyPrint = true;
            var nonDefaultMembers = GetMapForOutput(false);
            return w.Write(nonDefaultMembers);
        }

        public void ApplyJson(string json)
        {
            var result = Config.ApplyJson(json, this);
            // replace myself's members with the new ones
            _members = result._members;
        }

        public static implicit operator ConfigObject(ExpandoObject exp)
        {
            return FromExpando(exp);
        }

        #region ctor

        /// <summary>
        ///     Create a <see cref="ConfigObject" /> instance.
        /// </summary>
        /// <param name="isDefault">Indicates whether the config object is the default config.</param>
        public ConfigObject(bool isDefault)
        {
            _isDefault = isDefault;
        }

        /// <summary>
        ///     Create a <see cref="ConfigObject" /> instance.
        /// </summary>
        public ConfigObject()
            : this(false)
        {
        }

        #endregion

        #region ICollection implementation

        public void Add(KeyValuePair<string, object> item)
        {
            _members.TryAdd(item.Key, new ConfigObjectMember(_isDefault, item.Value));
        }

        public void Clear()
        {
            _members.Clear();
        }

        public bool Contains(KeyValuePair<string, object> item)
        {
            return _members.ContainsKey(item.Key) && _members[item.Key] == item.Value;
        }

        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<string, object>>)_members.ToDictionary(a => a.Key, a => a.Value.Value)).CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<string, object> item)
        {
            ConfigObjectMember dummy;
            return _members.TryRemove(item.Key, out dummy);
        }

        public int Count
        {
            get { return _members.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        #endregion

        #region IDictionary implementation

        public void Add(string key, object value)
        {
            _members.TryAdd(key, new ConfigObjectMember(_isDefault, value));
        }

        public bool ContainsKey(string key)
        {
            return _members.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            ConfigObjectMember dummy;
            return _members.TryRemove(key, out dummy);
        }

        public object this[string key]
        {
            get
            {
                if (ReferenceEquals(key, null))
                {
                    throw new ArgumentNullException("key");
                }
                ConfigObjectMember member;
                if (_members.TryGetValue(key, out member))
                {
                    return _members[key].Value;
                }
                else
                {
                    throw new KeyNotFoundException();
                }
            }
            set
            {
                _members[key] = new ConfigObjectMember(_isDefault, value);
            }
        }

        public ICollection<string> Keys
        {
            get { return _members.Keys; }
        }

        public ICollection<object> Values
        {
            get { return _members.Values.Select(a => a.Value).ToArray(); }
        }

        public bool IsDefault
        {
            get { return _isDefault; }
        }

        public bool TryGetValue(string key, out object value)
        {
            ConfigObjectMember configObjectMember;
            if (_members.TryGetValue(key, out configObjectMember))
            {
                value = configObjectMember.Value;
                return true;
            }
            else
            {
                value = null;
                return false;
            }
        }

        #region ICloneable implementation

        public object Clone()
        {
            return Merger.Merge(new ConfigObject(), this);
        }

        #endregion

        #endregion

        #region operator +

        public static dynamic operator +(ConfigObject a, ConfigObject b)
        {
            return Merger.Merge(b, a);
        }

        public static dynamic operator +(dynamic a, ConfigObject b)
        {
            return Merger.Merge(b, a);
        }

        public static dynamic operator +(ConfigObject a, dynamic b)
        {
            return Merger.Merge(b, a);
        }

        #endregion

        public void Set(string key, object value, bool isFromDefaultConfig)
        {
            _members[key] = new ConfigObjectMember(isFromDefaultConfig, value);
        }

        // Add all kinds of datatypes we can cast it to, and return default values
        // cast to string will be null
        public static implicit operator string(ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return "";
            }
            else
            {
                throw new InvalidCastException();
            }
        }

        public static implicit operator string[](ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return new string[] { };
            }
            else
            {
                throw new InvalidCastException();
            }
        }

        // cast to bool will always be false
        public static implicit operator bool(ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        public static implicit operator bool[](ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return new bool[] { };
            }
            else
            {
                throw new InvalidCastException();
            }
        }

        public static implicit operator int[](ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return new int[] { };
            }
            else
            {
                throw new InvalidCastException();
            }
        }

        public static implicit operator long[](ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return new long[] { };
            }
            else
            {
                throw new InvalidCastException();
            }
        }

        public static implicit operator int(ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return 0;
            }
            else
            {
                throw new InvalidCastException();
            }
        }

        public static implicit operator long(ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return 0;
            }
            else
            {
                throw new InvalidCastException();
            }
        }

        // nullable types always return null
        public static implicit operator bool?(ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return null;
            }
            else
            {
                throw new InvalidCastException();
            }
        }

        public static implicit operator int?(ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return null;
            }
            else
            {
                throw new InvalidCastException();
            }
        }

        public static implicit operator long?(ConfigObject nep)
        {
            if (nep == null || nep._members.Count == 0)
            {
                return null;
            }
            else
            {
                throw new InvalidCastException();
            }
        }
    }

    ///// <summary>
    /////     Null exception preventer. This allows for hassle-free usage of configuration values that are not
    /////     defined in the config file. I.e. we can do Config.Scope.This.Field.Does.Not.Exist.Ever, and it will
    /////     not throw an NullPointer exception, but return te NullExceptionPreventer object instead.
    /////     The NullExceptionPreventer can be cast to everything, and will then return default/empty value of
    /////     that datatype.
    ///// </summary>
    //public class NullExceptionPreventer : DynamicObject
    //{
    //    public NullExceptionPreventer()
    //    {
            
    //    }
    //    // all member access to a NullExceptionPreventer will return a new NullExceptionPreventer
    //    // this allows for infinite nesting levels: var s = Obj1.foo.bar.bla.blubb; is perfectly valid
    //    public override bool TryGetMember(GetMemberBinder binder, out object result)
    //    {
    //        result = new NullExceptionPreventer();
    //        return true;
    //    }

    //    // Add all kinds of datatypes we can cast it to, and return default values
    //    // cast to string will be null
    //    public static implicit operator string(NullExceptionPreventer nep)
    //    {
    //        return "";
    //    }

    //    public override string ToString()
    //    {
    //        return "";
    //    }

    //    public static implicit operator string[](NullExceptionPreventer nep)
    //    {
    //        return new string[] { };
    //    }

    //    // cast to bool will always be false
    //    public static implicit operator bool(NullExceptionPreventer nep)
    //    {
    //        return false;
    //    }

    //    public static implicit operator bool[](NullExceptionPreventer nep)
    //    {
    //        return new bool[] { };
    //    }

    //    public static implicit operator int[](NullExceptionPreventer nep)
    //    {
    //        return new int[] { };
    //    }

    //    public static implicit operator long[](NullExceptionPreventer nep)
    //    {
    //        return new long[] { };
    //    }

    //    public static implicit operator int(NullExceptionPreventer nep)
    //    {
    //        return 0;
    //    }

    //    public static implicit operator long(NullExceptionPreventer nep)
    //    {
    //        return 0;
    //    }

    //    // nullable types always return null
    //    public static implicit operator bool?(NullExceptionPreventer nep)
    //    {
    //        return null;
    //    }

    //    public static implicit operator int?(NullExceptionPreventer nep)
    //    {
    //        return null;
    //    }

    //    public static implicit operator long?(NullExceptionPreventer nep)
    //    {
    //        return null;
    //    }
    //}
}