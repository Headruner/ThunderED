﻿using System;
using System.Linq;
using System.Threading.Tasks;
using ThunderED.Classes;
using ThunderED.Classes.Enums;
using ThunderED.Helpers;
using ThunderED.Modules.Sub;

namespace ThunderED.Modules
{
    public partial class ContractNotificationsModule
    {
        private async Task WebPartInitialization()
        {
            if (WebServerModule.WebModuleConnectors.ContainsKey(Reason))
                WebServerModule.WebModuleConnectors.Remove(Reason);
            WebServerModule.WebModuleConnectors.Add(Reason, ProcessRequest);
            await Task.CompletedTask;
        }

        public ContractNotifyGroup WebGetAuthGroup(long userId, out string groupName)
        {
            groupName = null;
            var name = this.GetAllParsedCharactersWithGroups().FirstOrDefault(a => a.Value.Contains(userId)).Key;
            if (string.IsNullOrEmpty(name)) return null;

            var grp =  Settings.ContractNotificationsModule.GetEnabledGroups()
                .FirstOrDefault(a => a.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

            groupName = grp.Key;
            return grp.Value;
        }

        public async Task<WebQueryResult> ProcessRequest(string query, CallbackTypeEnum type, string ip, WebAuthUserData data)
        {
            if (!Settings.Config.ModuleContractNotifications)
                return WebQueryResult.False;

            try
            {
                RunningRequestCount++;
                if (query.Contains("&state=cauth"))
                {
                    var prms = query.TrimStart('?').Split('&');
                    var code = prms[0].Split('=')[1];

                    var result = await WebAuthModule.GetCharacterIdFromCode(code,
                        Settings.WebServerModule.CcpAppClientId, Settings.WebServerModule.CcpAppSecret);
                    if (result == null)
                        return WebQueryResult.EsiFailure;

                    var characterId = result[0];
                    var numericCharId = Convert.ToInt64(characterId);

                    if (string.IsNullOrEmpty(characterId))
                    {
                        await LogHelper.LogWarning("Bad or outdated contract feed request!");
                        var r = WebQueryResult.BadRequestToSystemAuth;
                        r.Message1 = LM.Get("accessDenied");
                        return r;
                    }

                    if (!HasAuthAccess(numericCharId))
                    {
                        await LogHelper.LogWarning($"Unauthorized contracts feed request from {characterId}");
                        var r = WebQueryResult.BadRequestToSystemAuth;
                        r.Message1 = LM.Get("accessDenied");
                        return r;
                    }

                    if (WebGetAuthGroup(numericCharId, out _)==null)
                    {
                        await LogHelper.LogWarning("Contract auth group not found!");
                        var r = WebQueryResult.BadRequestToSystemAuth;
                        r.Message1 = LM.Get("accessDenied");
                        return r;
                    }

                    // var rChar = await APIHelper.ESIAPI.GetCharacterData(Reason, characterId, true);

                    var t = await DbHelper.UpdateToken(result[1], numericCharId, TokenEnum.Contract);
                    var accessToken = (await APIHelper.ESIAPI.GetAccessToken(t))?.Result;
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        t.Scopes = APIHelper.ESIAPI.GetScopesFromToken(accessToken);
                        await DbHelper.UpdateToken(t.Token, t.CharacterId,t.Type, t.Scopes);
                    }


                    await LogHelper.LogInfo($"Contracts feed added for character: {characterId}", LogCat.AuthWeb);

                    var res = WebQueryResult.FeedAuthSuccess;
                    res.Message1 = LM.Get("contractAuthSuccessHeader");
                    res.Message2 = LM.Get("contractAuthSuccessBody");
                    return res;
                }
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(ex.Message, ex, Category);
            }
            finally
            {
                RunningRequestCount--;
            }

            return WebQueryResult.False;
        }
    }
}
