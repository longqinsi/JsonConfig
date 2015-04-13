JsonConfig README
=====================

## About
The project is ported from [JsonConfig](https://github.com/Dynalon/JsonConfig) by [Timo Dörr](https://github.com/Dynalon). The basic usage can be found [there](https://github.com/Dynalon/JsonConfig). But there are some small changes in this port. The modified usage will be added later.
The original JsonConfig is great, but when I find that with it I can create only one Default config, only one User config and only one Global config in an AppDomain, I decides to create this new port. The main difference to original JsonConfig is that now you could have Default/User config per assembly, plus a global Default/User config. Additionally, I think there is no need to have a Global config given there is a User config, I removed it, and the User config is config merged from user config file and default config. At last, I also defined a plus(+) operator for ConfigObject class to simply Merge operations.