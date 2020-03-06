using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;

namespace dvdrip
{
    public enum mqttNotifyState
    {
        updateMKV,
        rip,
        compress,
        copy,
        complete,
        generic
    }
    public class NotifyMQTT
    {
        public async void Notify(mqttNotifyState state,string payload)
        {
            MQTTnet.IMqttFactory factory = new MqttFactory();
            IMqttClient client = factory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer("192.168.1.67", 1883)
                .WithCredentials("zach", "Lfjb3Xyky")
                .Build();

            await client.ConnectAsync(options, System.Threading.CancellationToken.None);

            string topic;
            switch (state)
            {
                case mqttNotifyState.updateMKV:
                    topic = "dvdrip/updateMKV";
                    break;
                case mqttNotifyState.rip:
                    topic = "dvdrip/rip";
                    break;
                case mqttNotifyState.compress:
                    topic = "dvdrip/compress";
                    break;
                case mqttNotifyState.copy:
                    topic = "dvdrip/copy";
                    break;
                case mqttNotifyState.complete:
                    topic = "dvdrip/complete";
                    break;
                case mqttNotifyState.generic:
                    topic = "dvdrip/generic";
                    break;
                default:
                    topic = "dvdrip/generic";
                    break;
            }

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithExactlyOnceQoS()
                .WithRetainFlag()
                .Build();

            await client.PublishAsync(message);


        }

    }
}
