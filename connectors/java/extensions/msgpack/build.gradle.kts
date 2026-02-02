plugins {
    java
    `java-library`
}

group = "com.playhouse"
version = "0.1.0"

java {
    sourceCompatibility = JavaVersion.VERSION_22
    targetCompatibility = JavaVersion.VERSION_22
}

repositories {
    mavenCentral()
}

dependencies {
    // Reference to the main connector module
    api(project(":"))

    // MessagePack serialization
    implementation("org.msgpack:msgpack-core:0.9.6")
    implementation("org.msgpack:jackson-dataformat-msgpack:0.9.6")
    implementation("com.fasterxml.jackson.core:jackson-databind:2.16.0")

    // Testing
    testImplementation("org.junit.jupiter:junit-jupiter:5.10.0")
    testImplementation("org.assertj:assertj-core:3.24.2")
}

tasks.test {
    useJUnitPlatform()
}

tasks.withType<JavaCompile> {
    options.encoding = "UTF-8"
}
