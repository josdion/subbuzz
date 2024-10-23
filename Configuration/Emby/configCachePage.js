define(['jQuery', 'loading', 'mainTabsManager', 'globalize'], function ($, loading, mainTabsManager, globalize) {
    'use strict';

    var SubbuzzConfig = {
        pluginUniqueId: '5aeab01b-2ef8-45c6-bb6b-16ce9cb268d4'
    };

    function onViewShow() {

        mainTabsManager.setTabs(this, 1, getTabs);
        var page = this;

        $('#SelectCachePath', page).html('<i class="md-icon">search</i>');

        loading.show();
        ApiClient.getPluginConfiguration(SubbuzzConfig.pluginUniqueId).then(function (config) {

            page.querySelector("#SubtitleCache").checked = config.Cache.Subtitle;
            page.querySelector("#SubLifeInMinutes").value = config.Cache.SubLifeInMinutes;
            page.querySelector("#SearchCache").checked = config.Cache.Search;
            page.querySelector("#SearchLifeInMinutes").value = config.Cache.SearchLifeInMinutes;
            page.querySelector('#CachePath').value = config.Cache.BasePath || '';

            loading.hide();
        });
    }

    function onSubmit(e) {

        loading.show();

        var form = this;

        ApiClient.getPluginConfiguration(SubbuzzConfig.pluginUniqueId).then(function (config) {

            var saveConfig = function () {

                config.Cache.Subtitle = form.querySelector("#SubtitleCache").checked;
                config.Cache.SubLifeInMinutes = form.querySelector("#SubLifeInMinutes").value;
                config.Cache.Search = form.querySelector("#SearchCache").checked;
                config.Cache.SearchLifeInMinutes = form.querySelector("#SearchLifeInMinutes").value;
                config.Cache.BasePath = form.querySelector('#CachePath').value;

                ApiClient.updatePluginConfiguration(SubbuzzConfig.pluginUniqueId, config).then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                });
            }

            saveConfig();
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

        view.addEventListener('viewshow', onViewShow);

        view.querySelector('#SelectCachePath').addEventListener('click', function () {
            require(['directorybrowser'], function (directoryBrowser) {
                var picker = new directoryBrowser();
                picker.show({
                    callback: function (path) {
                        if (path) {
                            view.querySelector('#CachePath').value = path;
                        }
                        picker.close();
                    },
                    header: 'Select Cache Path',
                    instruction: '',
                    validateWriteable: true,
                });
            });
        });


    };

});
