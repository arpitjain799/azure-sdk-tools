﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using APIView;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ApiView
{
    public class CodeFile
    {
        private static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions()
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private string _versionString;

        [Obsolete("This is only for back compat, VersionString should be used")]
        public int Version { get; set; }

        public string VersionString
        {
#pragma warning disable 618
            get => _versionString ?? Version.ToString();
#pragma warning restore 618
            set => _versionString = value;
        }

        public string Name { get; set; }

        public string Language { get; set; }

        public string LanguageVariant { get; set; }

        public string PackageName { get; set; }

        public string ServiceName { get; set; }

        public string PackageDisplayName { get; set; }

        public CodeFileToken[] Tokens { get; set; } = Array.Empty<CodeFileToken>();

        public List<CodeFileToken[]> Sections { get; set; }

        public NavigationItem[] Navigation { get; set; }

        public CodeDiagnostic[] Diagnostics { get; set; }

        public override string ToString()
        {
            return new CodeFileRenderer().Render(this).CodeLines.ToString();
        }

        public static async Task<CodeFile> DeserializeAsync(Stream stream, bool hasSections = false)
        {
            var codeFile = await JsonSerializer.DeserializeAsync<CodeFile>(
                stream,
                JsonSerializerOptions);

            if (hasSections)
            {
                var index = 0;
                var tokens = codeFile.Tokens;
                var newTokens = new List<CodeFileToken>();
                var allSections = new List<CodeFileToken[]>();
                var section = new List<CodeFileToken>();
                var isLeaf = false;

                while (index < tokens.Length)
                {
                    var token = tokens[index];
                    if (token.Kind == CodeFileTokenKind.FoldableSectionHeading)
                    {
                        section.Add(token);
                        newTokens.AddRange(section);
                        section.Clear();
                        isLeaf = false;
                    }
                    else if (token.Kind == CodeFileTokenKind.FoldableSectionContentStart)
                    {
                        section.Add(token);
                        isLeaf = true;
                    }
                    else if (token.Kind == CodeFileTokenKind.FoldableSectionContentEnd)
                    {
                        section.Add(token);
                        if (isLeaf)
                        {
                            allSections.Add(section.ToArray());
                            section.Clear();
                            isLeaf = false;
                        }
                    }
                    else
                    {
                        section.Add(token);
                    }
                    index++;
                }
                newTokens.AddRange(section);
                codeFile.Tokens = newTokens.ToArray();
                codeFile.Sections = allSections;
            }
            return codeFile;
        }

        public async Task SerializeAsync(Stream stream)
        {
            await JsonSerializer.SerializeAsync(
                stream,
                this,
                JsonSerializerOptions);
        }
    }
}
