﻿
using System;
using System.Collections.Generic;

namespace HLDParser
{
    internal interface ITreeBuildSupport
    {
        CKey GetTree(string inFileName);
        ILogger GetLogger();
    }

    internal static class CTreeBuilder
    {
        enum EMultiArrayType { NotMultiArray, List, MultiArray }

        class CBuildCommands
        {
            ILogger _logger;

            public CBuildCommands(ILogger logger)
            {
                _logger = logger;
            }

            #region WriteComments
            List<Tuple<int, string>> _add_comment = new List<Tuple<int, string>>();
            public void AddComment(int line, string name) { _add_comment.Add(new Tuple<int, string>(line, name)); }

            internal void WriteComments(CBaseKey root)
            {
                foreach (var t in _add_comment)
                {
                    CBaseKey fk = root.FindLowerNearestKey(t.Item1).Item1;
                    if (fk == null)
                        _logger.LogError(EErrorCode.CantAddComment, t.Item2, t.Item1);
                    else
                        fk.AddComments(t.Item2);
                }

                _add_comment.Clear();
            }
            #endregion Comments

            string _next_array_key_name;
            CBaseKey _next_array_key_parent;
            int _next_array_key_line_number;
            public bool IsNextArrayKeyNamePresent { get { return !string.IsNullOrEmpty(_next_array_key_name); } }

            public void SetNextArrayKeyName(string inName, int inLineNumber, CBaseKey inParent)
            {
                if (IsNextArrayKeyNamePresent)
                    _logger.LogError(EErrorCode.NextArrayKeyNameAlreadySetted, _next_array_key_name, inLineNumber);
                else
                {
                    _next_array_key_name = inName;
                    _next_array_key_parent = inParent;
                    _next_array_key_line_number = inLineNumber;
                }
            }

            public string PopNextArrayKeyName(CBaseKey inParent)
            {
                if(_next_array_key_parent != inParent)
                {
                    _logger.LogError(EErrorCode.NextArrayKeyNameMissParent,
                        string.Format("Name {0}. Setted inside {1}. Try to use inside {2}", 
                        _next_array_key_name,
                        _next_array_key_parent.Name,
                        inParent.Name), 
                        _next_array_key_line_number);
                    _next_array_key_name = string.Empty;
                    return string.Empty;
                }

                string t = _next_array_key_name;
                _next_array_key_name = string.Empty;
                return t;
            }
        }

        public static CKey Build(List<CTokenLine> inLines, ITreeBuildSupport inSupport)
        {
            var root = new CKey();

            CBuildCommands commands = new CBuildCommands(inSupport.GetLogger());

            Collect(root, -1, inLines, 0, inSupport, commands);
            root.CheckOnOneArray();

            if(root.KeyCount == 1 && root.GetKey(0).GetElementType() == EElementType.Key)
            {
                root = root.GetKey(0) as CKey;
                root.SetParent(null);
            }

            commands.WriteComments(root);

            return root;
        }

        static int Collect(CKey inParent, int inParentRank, List<CTokenLine> inLines, int inStartIndex, ITreeBuildSupport inSupport, CBuildCommands inCommands)
        {
            CArrayKey ar_key = null;
            CKey last_key = null;
            EKeyAddingMode last_key_add_mode = EKeyAddingMode.AddUnique;

            int curr_rank = inParentRank + 1;

            int i = inStartIndex;
            while (i < inLines.Count)
            {
                int t = i;
                CTokenLine line = inLines[i];
                if (!line.IsEmpty())
                {
                    if (line.Rank < curr_rank)
                    {
                        OnClosingKey(last_key, last_key_add_mode, inSupport);
                        return i;
                    }
                    else if (line.Rank > curr_rank)
                    {
                        if (last_key == null)
                        {
                            inSupport.GetLogger().LogError(EErrorCode.TooDeepRank, line);
                        }
                        else
                        {
                            i = Collect(last_key, curr_rank, inLines, i, inSupport, inCommands);
                            last_key.CheckOnOneArray();
                        }
                    }
                    else
                    {
                        OnClosingKey(last_key, last_key_add_mode, inSupport);

                        Tuple<CArrayKey, CKey, EKeyAddingMode> res = AddLine(inParent, ar_key, line, inSupport, inCommands);
                        ar_key = res.Item1;
                        last_key = res.Item2;
                        last_key_add_mode = res.Item3;
                    }
                }
                else if (line.Comments != null)
                    inCommands.AddComment(line.Position.Line, line.Comments.Text);

                if (t == i)
                    i++;
            }

            //if(last_key != null)
            //    last_key.CheckOnOneArray(inSupport);
            OnClosingKey(last_key, last_key_add_mode, inSupport);
            return i;
        }

        static void OnClosingKey(CBaseKey key, EKeyAddingMode inKeyAddMode, ITreeBuildSupport inSupport)
        {
            if (key == null)
                return;

            if (key.IsEmpty)
            {
                inSupport.GetLogger().LogError(EErrorCode.HeadWithoutValues, key);
                return;
            }

            if (inKeyAddMode == EKeyAddingMode.AddUnique)
                return;

            CBaseKey parent = key.Parent;
            if (parent == null)
            {
                inSupport.GetLogger().LogError(EErrorCode.KeyMustHaveParent, key);
                return;
            }

            key.SetParent(null);

            if (inKeyAddMode == EKeyAddingMode.Add)
            {
                CBaseKey child_key = parent.FindChildKey(key.Name);
                if (child_key != null)
                    child_key.MergeKey(key);
                else
                    key.SetParent(parent);
            }
            //if (inKeyAddMode == EKeyAddingMode.Override)
            //{
            //    parent.OverrideKey(key);
            //}
        }

        static CArrayKey CreateNewArrayKey(CBaseKey inParent, CTokenLine line, CBuildCommands inCommands)
        {
            var res_arr_key = new CArrayKey(inParent, line.Position);
            if (inCommands.IsNextArrayKeyNamePresent)
                res_arr_key.SetName(inCommands.PopNextArrayKeyName(inParent));
            return res_arr_key;
        }

        static Tuple<CArrayKey, CKey, EKeyAddingMode> AddLine(CBaseKey inParent, CArrayKey arr_key, CTokenLine line, ITreeBuildSupport inSupport, CBuildCommands inCommands)
        {
            CKey key = null;
            CArrayKey res_arr_key = null;
            EKeyAddingMode addition_mode = EKeyAddingMode.AddUnique;
            if (line.IsEmpty())
            {
                res_arr_key = arr_key;
            }
            else if (line.IsRecordDivider())
            {
                res_arr_key = null;
            }
            else if (line.IsCommandLine())
            {
                res_arr_key = ExecuteCommand(inParent, arr_key, line, inSupport, inCommands);
            }
            else if (line.Head != null)
            {
                res_arr_key = arr_key;
                if (res_arr_key == null)
                    res_arr_key = CreateNewArrayKey(inParent, line, inCommands);

                if (line.AdditionMode == EKeyAddingMode.AddUnique && res_arr_key.IsKeyWithNamePresent(line.Head.Text))
                    inSupport.GetLogger().LogError(EErrorCode.ElementWithNameAlreadyPresent, line);

                addition_mode = line.AdditionMode;
                key = new CKey(res_arr_key, line, inSupport.GetLogger());
            }
            else if(!line.IsTailEmpty)
            {
                if (line.TailLength == 1 && inParent.GetElementType() == EElementType.Key)
                    inParent.AddTokenTail(line, true, inSupport.GetLogger());
                else
                {
                    res_arr_key = CreateNewArrayKey(inParent, line, inCommands);
                    res_arr_key.AddTokenTail(line, false, inSupport.GetLogger());
                }
            }

            return new Tuple<CArrayKey, CKey, EKeyAddingMode>(res_arr_key, key, addition_mode);
        }

        static CArrayKey ExecuteCommand(CBaseKey inParent, CArrayKey inArrKey, CTokenLine line, ITreeBuildSupport inSupport, CBuildCommands inCommands)
        {
            CArrayKey arr_key = inArrKey;

            if (line.Command == ECommands.Name)
            {
                if (line.CommandParams.Length < 1)
                    inSupport.GetLogger().LogError(EErrorCode.EmptyCommand, line);
                else
                    inCommands.SetNextArrayKeyName(line.CommandParams[0], line.Position.Line, inParent);
            }
            else if (line.Command == ECommands.Insert)
            {
                if (arr_key == null)
                    arr_key = CreateNewArrayKey(inParent, line, inCommands);

                ExecuteCommand_Insert(arr_key, line, inSupport);
            }
            else if (line.Command == ECommands.Delete)
            {
                if (arr_key == null)
                    arr_key = CreateNewArrayKey(inParent, line, inCommands);

                ExecuteCommand_Delete(arr_key, line, inSupport);
            }
            return arr_key;
        }

        static void ExecuteCommand_Delete(CArrayKey arr_key, CTokenLine line, ITreeBuildSupport inSupport)
        {
            if (line.CommandParams.Length == 0 || string.IsNullOrEmpty(line.CommandParams[0]))
            {
                inSupport.GetLogger().LogError(EErrorCode.LocalPathEmpty, line);
                return;
            }

            string key_path = line.CommandParams[0];

            string[] path = key_path.Split(new char[] { '\\', '/' });

            if(!RemoveKeysByPath(arr_key, path))
                inSupport.GetLogger().LogError(EErrorCode.CantFindKey, line);
        }

        static bool RemoveKeysByPath(CBaseKey inParent, string[] inPath)
        {
            CBaseKey key = inParent.FindKey(inPath);
            if (key == null)
                return false;

            CBaseKey parent = key.Parent;
            key.SetParent(null);
            while (parent != inParent && parent.IsEmpty)
            {
                CBaseKey prev = parent;
                parent = parent.Parent;
                prev.SetParent(null);
            }
            return true;
        }

        static void ExecuteCommand_Insert(CArrayKey arr_key, CTokenLine line, ITreeBuildSupport inSupport)
        {
            string key_path = line.CommandParams["key"];

            if (string.IsNullOrEmpty(key_path))
                inSupport.GetLogger().LogError(EErrorCode.LocalPathEmpty, line);

            string file_name = line.CommandParams["file"];
            CBaseKey root = null;
            if (!string.IsNullOrEmpty(file_name))
                root = inSupport.GetTree(file_name);
            else
                root = arr_key.GetRoot();

            if (root == null)
            {
                inSupport.GetLogger().LogError(EErrorCode.CantFindRootInFile, line);
                return;
            }

            string[] path = key_path.Split(new char[] { '\\', '/' });

            CBaseKey key = root.FindKey(path);
            if (key == null)
            {
                inSupport.GetLogger().LogError(EErrorCode.CantFindKey, line);
                return;
            }


            bool insert_only_elements = line.CommandParams.ContainsKey("elems");
            CBaseKey copy_key = key.GetCopy() as CBaseKey;
            if (!insert_only_elements)
                copy_key.SetParent(arr_key);
            else
                arr_key.TakeAllElements(copy_key, false);
            arr_key.CheckOnOneArray();
        }

        struct SCommandParams
        {
            public string file_name;
            public string key_path;
            public bool insert_only_elements;
        }

        static SCommandParams GetFileAndKeys(CTokenLine line, ITreeBuildSupport inSupport)
        {
            if (line.CommandParams.Length < 1)
            {
                inSupport.GetLogger().LogError(EErrorCode.EmptyCommand, line);
                return new SCommandParams
                {
                    file_name = string.Empty,
                    key_path = string.Empty,
                    insert_only_elements = false,
                };
            }

            string fn = string.Empty;
            string kp = string.Empty;
            bool only_elements = false;

            if (line.CommandParams.Length > 2)
            {
                fn = line.CommandParams[0];
                kp = line.CommandParams[1];
                only_elements = true;
            }
            else if (line.CommandParams.Length == 2)
            {
                fn = line.CommandParams[0];
                kp = line.CommandParams[1];
            }
            else if (line.CommandParams.Length == 1)
            {
                kp = line.CommandParams[0];
            }

            return new SCommandParams
            {
                file_name = fn,
                key_path = kp,
                insert_only_elements = only_elements,
            };
        }
    }
}

