﻿'use strict';

define([
    'vent',
    'AppLayout',
    'marionette',
    'Settings/Indexers/Delete/IndexerDeleteView',
    'Commands/CommandController',
    'Mixins/AsModelBoundView',
    'Mixins/AsValidatedView',
    'underscore',
    'Form/FormBuilder',
    'Mixins/AutoComplete',
    'bootstrap'
], function (vent, AppLayout, Marionette, DeleteView, CommandController, AsModelBoundView, AsValidatedView, _) {

    var view = Marionette.ItemView.extend({
        template: 'Settings/Indexers/Edit/IndexerEditViewTemplate',

        events: {
            'click .x-save'        : '_save',
            'click .x-save-and-add': '_saveAndAdd',
            'click .x-delete'      : '_delete',
            'click .x-back'        : '_back',
           'click .x-test'        : '_test'
        },

        initialize: function (options) {
            this.targetCollection = options.targetCollection;
        },

        _save: function () {
            var self = this;
            var promise = this.model.save();

            if (promise) {
                promise.done(function () {
                    self.targetCollection.add(self.model, { merge: true });
                    vent.trigger(vent.Commands.CloseModalCommand);
                });
            }
        },

        _saveAndAdd: function () {
            var self = this;
            var promise = this.model.save();

            if (promise) {
                promise.done(function () {
                    self.targetCollection.add(self.model, { merge: true });

                    require('Settings/Indexers/Add/IndexerSchemaModal').open(self.targetCollection);
                });
            }
        },

        _delete: function () {
            var view = new DeleteView({ model: this.model });
            AppLayout.modalRegion.show(view);
        },

        _back: function () {
            if (this.model.isNew()) {
                this.model.destroy();
            }

            require('Settings/Indexers/Add/IndexerSchemaModal').open(this.targetCollection);
        },

        _test: function () {
            var testCommand = 'test{0}'.format(this.model.get('implementation'));
            var properties = {};

            _.each(this.model.get('fields'), function (field) {
                properties[field.name] = field.value;
            });

            CommandController.Execute(testCommand, properties);
        }
    });

    AsModelBoundView.call(view);
    AsValidatedView.call(view);

    return view;
});
