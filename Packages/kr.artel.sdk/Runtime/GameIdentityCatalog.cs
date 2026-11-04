using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Artel.Protocol.Dto;

namespace Artel
{
    internal static class GameIdentityCatalog
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
                                             BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private static readonly Regex NamePattern = new Regex(@"\A[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+\z");
        private static readonly Regex IdentifiesPattern = new Regex(@"\A[a-z0-9]+([ -][a-z0-9]+)*\z");

        public static bool TryCollect(out GameIdentityCatalogMessageDto catalog, out string error)
        {
            return TryCollect(LoadedTypes(), out catalog, out error);
        }

        private static IEnumerable<Type> LoadedTypes()
        {
            var runtime = typeof(ArtelMetadataAttribute).Assembly;
            var runtimeName = runtime.GetName().Name;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly != runtime && !Array.Exists(assembly.GetReferencedAssemblies(), reference => reference.Name == runtimeName))
                    continue;
                // 일부 type만 읽고 보내면 서버가 빠진 선언을 삭제하므로 GetTypes 실패를 그대로 전파한다.
                foreach (var type in assembly.GetTypes()) yield return type;
            }
        }

        internal static bool TryCollect(IEnumerable<Type> types, out GameIdentityCatalogMessageDto catalog, out string error)
        {
            catalog = null;
            error = null;
            try
            {
                var entries = new List<GameIdentityEntryDto>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var type in types)
                {
                    foreach (var member in type.GetMembers(Members))
                    {
                        if (!(member is FieldInfo) && !(member is PropertyInfo)) continue;
                        foreach (var attribute in CustomAttributeData.GetCustomAttributes(member))
                        {
                            if (!typeof(ArtelMetadataAttribute).IsAssignableFrom(attribute.AttributeType)) continue;
                            var entry = Read(member, attribute);
                            Validate(entry);
                            if (!names.Add(entry.Name))
                                throw new InvalidOperationException("Duplicate variable metadata name: " + entry.Name);
                            entries.Add(entry);
                        }
                    }
                }
                entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
                catalog = new GameIdentityCatalogMessageDto { Identities = entries };
                return true;
            }
            catch (Exception exception)
            {
                // 불완전한 목록은 빈 목록과 구분해 전체 전송을 중단한다.
                error = exception.Message;
                return false;
            }
        }

        private static GameIdentityEntryDto Read(MemberInfo member, CustomAttributeData attribute)
        {
            var entry = new GameIdentityEntryDto { Name = member.DeclaringType.Name + "." + member.Name };
            foreach (var argument in attribute.NamedArguments)
            {
                var value = argument.TypedValue.Value;
                switch (argument.MemberName)
                {
                    case nameof(ArtelMetadataAttribute.Kind): entry.Kind = (string)value; break;
                    case nameof(ArtelMetadataAttribute.Base): entry.Base = (int)value; break;
                    case nameof(ArtelMetadataAttribute.Meaning): entry.Meaning = (string)value; break;
                    case nameof(ArtelMetadataAttribute.ChangesWhen): entry.ChangesWhen = (string)value; break;
                    case nameof(ArtelMetadataAttribute.Identifies): entry.Identifies = (string)value; break;
                    case nameof(ArtelMetadataAttribute.Role): entry.Role = (string)value; break;
                }
            }
            return entry;
        }

        internal static void Validate(GameIdentityEntryDto entry)
        {
            Require(entry.Name.Length <= 200 && NamePattern.IsMatch(entry.Name), entry.Name, "name");
            Require(entry.Kind == "ordinal" || entry.Kind == "enum" || entry.Kind == "id" ||
                    entry.Kind == "flag" || entry.Kind == "text", entry.Name, "kind");
            Require(!entry.Base.HasValue || entry.Base == 0 || entry.Base == 1, entry.Name, "base");
            Require(!string.IsNullOrWhiteSpace(entry.Meaning) && entry.Meaning.Length <= 500, entry.Name, "meaning");
            Require(!string.IsNullOrWhiteSpace(entry.ChangesWhen) && entry.ChangesWhen.Length <= 500, entry.Name, "changesWhen");
            Require(entry.Identifies != null && entry.Identifies.Length <= 60 && IdentifiesPattern.IsMatch(entry.Identifies),
                entry.Name, "identifies");
            Require(entry.Role == "current" || entry.Role == "progress" || entry.Role == "other", entry.Name, "role");
        }

        private static void Require(bool valid, string name, string field)
        {
            if (!valid) throw new InvalidOperationException("Invalid variable metadata " + name + ": " + field);
        }
    }
}
