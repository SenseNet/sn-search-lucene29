﻿/* 
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 * 
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Diagnostics;
using System.Text;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using SenseNet.Diagnostics;

// ReSharper disable once CheckNamespace
namespace Contrib.Regex
{
    /// <summary>
    /// Regular expression based query.
    /// </summary>
    /// <remarks>http://www.java2s.com/Open-Source/Java-Document/Net/lucene-connector/org/apache/lucene/search/regex/RegexQuery.java.htm</remarks>
    public class RegexQuery : MultiTermQuery, IRegexQueryCapable, IEquatable<RegexQuery>
    {
        private IRegexCapabilities _regexImpl = new CSharpRegexCapabilities();
        //public Term Term { get; private set; }

        public RegexQuery(Term term)
        {
            base.term = term;
        }

        /// <summary>Construct the enumeration to be used, expanding the pattern term. </summary>
        public /*protected*/ override FilteredTermEnum GetEnum(IndexReader reader)
        {
            return new RegexTermEnum(reader, term, _regexImpl);
        }

        public IRegexCapabilities RegexImplementation
        {
            set { _regexImpl = value; }
            get { return _regexImpl; }
        }


        public override String ToString(String field)
        {
            StringBuilder buffer = new StringBuilder();
            if (!term.Field().Equals(field))
            {
                buffer.Append(term.Field());
                buffer.Append(":");
            }
            buffer.Append(term.Text());
            buffer.Append(ToStringUtils.Boost(GetBoost()));
            return buffer.ToString();
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other"/> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object</param>
        public bool Equals(RegexQuery other)
        {
            if (other == null) return false;
            if (this == other) return true;

            if (!base.Equals(other)) return false;
            return _regexImpl.Equals(other._regexImpl);
        }

        public override bool Equals(object obj)
        {
            if ((obj == null) || (obj as RegexQuery == null)) return false;
            if (this == obj) return true;

            return Equals((RegexQuery)obj);
        }

        public override int GetHashCode()
        {
            return 29 * base.GetHashCode() + _regexImpl.GetHashCode();
        }
    }
}