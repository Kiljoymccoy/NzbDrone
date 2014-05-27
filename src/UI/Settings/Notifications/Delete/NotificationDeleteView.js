'use strict';

define([
    'vent',
    'marionette'
], function (vent, Marionette) {
    return Marionette.ItemView.extend({
        template: 'Settings/Notifications/Delete/NotificationDeleteViewTemplate',

        events: {
            'click .x-confirm-delete': '_delete'
        },

        _delete: function () {
            this.model.destroy({
                wait   : true,
                success: function () {
                    vent.trigger(vent.Commands.CloseModalCommand);
                }
            });
        }
    });
});
