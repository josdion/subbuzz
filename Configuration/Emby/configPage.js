define(['jQuery', 'loading', 'mainTabsManager', 'globalize'], function ($, loading, mainTabsManager, globalize) {
    'use strict';

    var SubbuzzConfig = {
        pluginUniqueId: '5aeab01b-2ef8-45c6-bb6b-16ce9cb268d4'
    };

    function onViewShow(page) {
        loading.show();

        ApiClient.getPluginConfiguration(SubbuzzConfig.pluginUniqueId).then(function (config) {
            page.querySelector("#EnableAddic7ed").checked = config.EnableAddic7ed;
            page.querySelector("#EnablePodnapisiNet").checked = config.EnablePodnapisiNet;
            page.querySelector("#EnableSubf2m").checked = config.EnableSubf2m;
            page.querySelector("#EnableSubssabbz").checked = config.EnableSubssabbz;
            page.querySelector("#EnableSubsunacsNet").checked = config.EnableSubsunacsNet;
            page.querySelector("#EnableYavkaNet").checked = config.EnableYavkaNet;
            page.querySelector("#EnableYifySubtitles").checked = config.EnableYifySubtitles;

            page.querySelector("#EnableOpenSubtitles").checked = config.EnableOpenSubtitles;
            page.querySelector('#OpenSubUserName').value = config.OpenSubUserName || '';
            page.querySelector('#OpenSubPassword').value = config.OpenSubPassword || '';
            page.querySelector('#OpenSubUseHash').checked = config.OpenSubUseHash;

            page.querySelector("#EnableSubdlCom").checked = config.EnableSubdlCom;
            page.querySelector('#SubdlApiKey').value = config.SubdlApiKey || '';

            page.querySelector("#EnableSubSource").checked = config.EnableSubSource;
            page.querySelector('#SubSourceApiKey').value = config.SubSourceApiKey || '';

            page.querySelector("#EncodeSubtitlesToUTF8").checked = config.SubPostProcessing.EncodeSubtitlesToUTF8;
            page.querySelector('#AutoDetectEncoding').checked = config.SubEncoding.AutoDetectEncoding;

            var encodingsHtml = config.SubEncoding.Encodings.map(function (e) {
                return '<option value="' + e + '">' + e + '</option>';
            }).join('');

            $('#DefaultEncoding', page).html(encodingsHtml).val(config.SubEncoding.DefaultEncoding || '');

            page.querySelector('#SubtitleInfoWithHtml').checked = config.SubtitleInfoWithHtml;

            page.querySelector("#AdjustDuration").checked = config.SubPostProcessing.AdjustDuration;
            page.querySelector("#AdjustDurationCps").value = config.SubPostProcessing.AdjustDurationCps;
            page.querySelector("#AdjustDurationExtendOnly").checked = config.SubPostProcessing.AdjustDurationExtendOnly;
            page.querySelector('#AdjustDurationCps').disabled = !config.SubPostProcessing.AdjustDuration;
            page.querySelector('#AdjustDurationExtendOnly').disabled = !config.SubPostProcessing.AdjustDuration;

            loading.hide();
        });
    }

    function onSubmit(e) {

        loading.show();

        var form = this;

        ApiClient.getPluginConfiguration(SubbuzzConfig.pluginUniqueId).then(function (config) {

            const username = form.querySelector('#OpenSubUserName').value;
            const password = form.querySelector('#OpenSubPassword').value;
            const apiKey = "";
            var token = config.OpenSubToken || '';

            if ((username || password) && (!username || !password)) {
                Dashboard.processErrorResponse({ statusText: "OpenSubtitles.com account info is incomplete!" });
                return;
            }

            if (config.OpenSubUserName != username || config.OpenSubPassword != password || config.OpenSubApiKey != apiKey) {
                token = '';
            }

            var saveConfig = function () {
                config.EnableAddic7ed = form.querySelector("#EnableAddic7ed").checked;
                config.EnablePodnapisiNet = form.querySelector("#EnablePodnapisiNet").checked;
                config.EnableSubf2m = form.querySelector("#EnableSubf2m").checked;
                config.EnableSubssabbz = form.querySelector("#EnableSubssabbz").checked;
                config.EnableSubsunacsNet = form.querySelector("#EnableSubsunacsNet").checked;
                config.EnableYavkaNet = form.querySelector("#EnableYavkaNet").checked;
                config.EnableYifySubtitles = form.querySelector("#EnableYifySubtitles").checked;

                config.EnableOpenSubtitles = form.querySelector("#EnableOpenSubtitles").checked;
                config.OpenSubUseHash = form.querySelector("#OpenSubUseHash").checked;
                config.OpenSubUserName = username;
                config.OpenSubPassword = password;
                config.OpenSubApiKey = apiKey;
                config.OpenSubToken = token;

                config.EnableSubdlCom = form.querySelector("#EnableSubdlCom").checked;
                config.SubdlApiKey = form.querySelector('#SubdlApiKey').value;

                config.EnableSubSource = form.querySelector("#EnableSubSource").checked;
                config.SubSourceApiKey = form.querySelector('#SubSourceApiKey').value;

                config.SubPostProcessing.EncodeSubtitlesToUTF8 = form.querySelector("#EncodeSubtitlesToUTF8").checked;
                config.SubPostProcessing.AdjustDuration = form.querySelector("#AdjustDuration").checked;
                config.SubPostProcessing.AdjustDurationCps = form.querySelector("#AdjustDurationCps").value;
                config.SubPostProcessing.AdjustDurationExtendOnly = form.querySelector("#AdjustDurationExtendOnly").checked;

                config.SubEncoding.AutoDetectEncoding = form.querySelector("#AutoDetectEncoding").checked;
                config.SubEncoding.DefaultEncoding = $('#DefaultEncoding', form).val();

                config.SubtitleInfoWithHtml = form.querySelector("#SubtitleInfoWithHtml").checked;

                ApiClient.updatePluginConfiguration(SubbuzzConfig.pluginUniqueId, config).then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                });
            }

            if (username && !token) {
                const el = form.querySelector('#ossresponse');
                const data = JSON.stringify({ Username: username, Password: password, ApiKey: apiKey });
                const url = ApiClient.getUrl('subbuzz/ValidateOpenSubtitlesLoginInfo');

                const handler = response => response.json().then(res => {
                    if (response.ok && !res.Message) {
                        token = res.Token;
                        saveConfig()
                        if (res.Token && res.Downloads) {
                            el.innerText = "OpenSubtitles.com account validated. You can download " + res.Downloads + " subtitles per day.";
                        }
                    }
                    else {
                        let msg = res.Message ?? JSON.stringify(res, null, 2);
                        if (msg == 'You cannot consume this service') {
                            msg = 'Invalid API key provided.';
                        }

                        el.innerHtml = "&nbsp;";
                        Dashboard.processErrorResponse({ statusText: "Request failed - " + msg });
                        return;
                    }
                });

                ApiClient.ajax({ type: 'POST', url, data, contentType: 'application/json' }).then(handler).catch(handler);
            }
            else {
                form.querySelector('#ossresponse').innerHtml = "&nbsp;";
                saveConfig()
            }

        });


        // Disable default form submission
        e.preventDefault();
        e.stopPropagation();
        return false;
    }

    function getTabs() {
        return [
            {
                href: Dashboard.getConfigurationPageUrl('SubbuzzConfigPage'),
                name: 'General'
            },
            {
                href: Dashboard.getConfigurationPageUrl('SubbuzzConfigCachePage'),
                name: 'Cache'
            }];
    }

    return function (view, params) {
        view.querySelector('form').addEventListener('submit', onSubmit);

        view.addEventListener('viewshow', function () {
            mainTabsManager.setTabs(this, 0, getTabs);
            onViewShow(view);
        });

        view.querySelector('#AdjustDuration').addEventListener('change', function (e) {
            this.form.querySelector('#AdjustDurationCps').disabled = !this.checked;
            this.form.querySelector('#AdjustDurationExtendOnly').disabled = !this.checked;
        });
    };

});
